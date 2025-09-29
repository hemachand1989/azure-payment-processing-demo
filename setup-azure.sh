#!/bin/bash

# Azure Payment Processing Demo - Complete Setup Script
# This script creates all necessary Azure resources

set -e # Exit on error

echo "==================================="
echo "Azure Payment Processing Demo Setup"
echo "==================================="
echo ""

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo -e "${RED}Error: Azure CLI is not installed${NC}"
    echo "Please install from: https://docs.microsoft.com/cli/azure/install-azure-cli"
    exit 1
fi

# Check if logged in to Azure
echo -e "${YELLOW}Checking Azure login status...${NC}"
az account show &> /dev/null || {
    echo -e "${RED}Not logged in to Azure${NC}"
    echo "Running 'az login'..."
    az login
}

echo -e "${GREEN}✓ Azure CLI authenticated${NC}"
echo ""

# Get subscription info
SUBSCRIPTION_NAME=$(az account show --query name -o tsv)
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
echo "Using subscription: $SUBSCRIPTION_NAME"
echo "Subscription ID: $SUBSCRIPTION_ID"
echo ""

# Prompt for configuration
read -p "Enter resource group name [rg-payment-demo]: " RESOURCE_GROUP
RESOURCE_GROUP=${RESOURCE_GROUP:-rg-payment-demo}

read -p "Enter Azure region [eastus]: " LOCATION
LOCATION=${LOCATION:-eastus}

# Generate unique names with random suffix
RANDOM_SUFFIX=$RANDOM
SERVICEBUS_NAMESPACE="sb-payment-${RANDOM_SUFFIX}"
EVENTGRID_TOPIC="egt-payment-events"
STORAGE_ACCOUNT="stpayment${RANDOM_SUFFIX}"
FUNCTION_APP="func-payment-${RANDOM_SUFFIX}"

echo ""
echo "Configuration:"
echo "  Resource Group: $RESOURCE_GROUP"
echo "  Location: $LOCATION"
echo "  Service Bus: $SERVICEBUS_NAMESPACE"
echo "  Event Grid Topic: $EVENTGRID_TOPIC"
echo "  Storage Account: $STORAGE_ACCOUNT"
echo "  Function App: $FUNCTION_APP"
echo ""

read -p "Proceed with these settings? (y/n) " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Setup cancelled"
    exit 1
fi

echo ""
echo "==================================="
echo "Starting Resource Creation"
echo "==================================="
echo ""

# Create Resource Group
echo -e "${YELLOW}Creating resource group...${NC}"
az group create \
  --name $RESOURCE_GROUP \
  --location $LOCATION \
  --output none
echo -e "${GREEN}✓ Resource group created${NC}"

# Create Service Bus Namespace
echo -e "${YELLOW}Creating Service Bus namespace (this may take a few minutes)...${NC}"
az servicebus namespace create \
  --name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --sku Standard \
  --output none
echo -e "${GREEN}✓ Service Bus namespace created${NC}"

# Create Service Bus Queue
echo -e "${YELLOW}Creating Service Bus queue...${NC}"
az servicebus queue create \
  --name payment-requests \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --enable-dead-lettering-on-message-expiration true \
  --max-delivery-count 5 \
  --default-message-time-to-live PT24H \
  --output none
echo -e "${GREEN}✓ Queue 'payment-requests' created${NC}"

# Create Service Bus Topic
echo -e "${YELLOW}Creating Service Bus topic...${NC}"
az servicebus topic create \
  --name payment-status \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --default-message-time-to-live PT24H \
  --output none
echo -e "${GREEN}✓ Topic 'payment-status' created${NC}"

# Create Topic Subscriptions
echo -e "${YELLOW}Creating topic subscriptions...${NC}"

az servicebus topic subscription create \
  --name payment-processor-sub \
  --topic-name payment-status \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --max-delivery-count 5 \
  --output none

az servicebus topic subscription create \
  --name notification-sub \
  --topic-name payment-status \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --max-delivery-count 5 \
  --output none

az servicebus topic subscription create \
  --name analytics-sub \
  --topic-name payment-status \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --max-delivery-count 5 \
  --output none

echo -e "${GREEN}✓ All subscriptions created${NC}"

# Create Event Grid Topic
echo -e "${YELLOW}Creating Event Grid topic...${NC}"
az eventgrid topic create \
  --name $EVENTGRID_TOPIC \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --output none
echo -e "${GREEN}✓ Event Grid topic created${NC}"

# Create Storage Account
echo -e "${YELLOW}Creating storage account...${NC}"
az storage account create \
  --name $STORAGE_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --sku Standard_LRS \
  --output none
echo -e "${GREEN}✓ Storage account created${NC}"

# Create Function App
echo -e "${YELLOW}Creating Function App...${NC}"
az functionapp create \
  --name $FUNCTION_APP \
  --resource-group $RESOURCE_GROUP \
  --storage-account $STORAGE_ACCOUNT \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --os-type Linux \
  --consumption-plan-location $LOCATION \
  --output none
echo -e "${GREEN}✓ Function App created${NC}"

# Get connection strings and keys
echo ""
echo -e "${YELLOW}Retrieving connection strings and keys...${NC}"

SB_CONNECTION=$(az servicebus namespace authorization-rule keys list \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --name RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv)

EG_ENDPOINT=$(az eventgrid topic show \
  --name $EVENTGRID_TOPIC \
  --resource-group $RESOURCE_GROUP \
  --query endpoint -o tsv)

EG_KEY=$(az eventgrid topic key list \
  --name $EVENTGRID_TOPIC \
  --resource-group $RESOURCE_GROUP \
  --query key1 -o tsv)

echo -e "${GREEN}✓ Connection strings retrieved${NC}"

# Configure Function App
echo -e "${YELLOW}Configuring Function App settings...${NC}"
az functionapp config appsettings set \
  --name $FUNCTION_APP \
  --resource-group $RESOURCE_GROUP \
  --settings \
    "ServiceBusConnection=$SB_CONNECTION" \
    "EventGridTopicEndpoint=$EG_ENDPOINT" \
    "EventGridTopicKey=$EG_KEY" \
    "ShippingWebhookUrl=https://webhook.site/your-shipping-webhook" \
    "InventoryWebhookUrl=https://webhook.site/your-inventory-webhook" \
  --output none
echo -e "${GREEN}✓ Function App configured${NC}"

# Save configuration to file
CONFIG_FILE="deployment-config.txt"
cat > $CONFIG_FILE <<EOF
Azure Payment Processing Demo - Deployment Configuration
Generated: $(date)

Resource Group: $RESOURCE_GROUP
Location: $LOCATION
Subscription: $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)

Service Bus Namespace: $SERVICEBUS_NAMESPACE
Event Grid Topic: $EVENTGRID_TOPIC
Storage Account: $STORAGE_ACCOUNT
Function App: $FUNCTION_APP

Service Bus Connection String:
$SB_CONNECTION

Event Grid Endpoint:
$EG_ENDPOINT

Event Grid Key:
$EG_KEY
EOF

echo -e "${GREEN}✓ Configuration saved to $CONFIG_FILE${NC}"

# Output summary
echo ""
echo "==================================="
echo "Setup Complete! 🎉"
echo "==================================="
echo ""
echo "Resources created:"
echo "  ✓ Resource Group: $RESOURCE_GROUP"
echo "  ✓ Service Bus Namespace: $SERVICEBUS_NAMESPACE"
echo "  ✓ Service Bus Queue: payment-requests"
echo "  ✓ Service Bus Topic: payment-status (with 3 subscriptions)"
echo "  ✓ Event Grid Topic: $EVENTGRID_TOPIC"
echo "  ✓ Storage Account: $STORAGE_ACCOUNT"
echo "  ✓ Function App: $FUNCTION_APP"
echo ""
echo "==================================="
echo "Next Steps"
echo "==================================="
echo ""
echo "1. Update webhook URLs:"
echo "   - Go to https://webhook.site to get test webhook URLs"
echo "   - Update Function App settings with real URLs"
echo ""
echo "2. Deploy your functions:"
echo "   cd PaymentProcessingFunctions"
echo "   func azure functionapp publish $FUNCTION_APP"
echo ""
echo "3. Test the API:"
echo "   API Endpoint: https://$FUNCTION_APP.azurewebsites.net/api/payments"
echo ""
echo "4. Monitor in Azure Portal:"
echo "   https://portal.azure.com/#resource/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
echo ""
echo "Configuration saved to: $CONFIG_FILE"
echo ""
echo "To delete all resources:"
echo "   az group delete --name $RESOURCE_GROUP --yes --no-wait"
echo ""
