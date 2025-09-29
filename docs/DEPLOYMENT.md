# Deployment Guide

## Error: "Unable to find project root"

If you get this error when running `func azure functionapp publish`, it means you're in the wrong directory.

## ✅ Correct Deployment Steps

### Step 1: Clone and Navigate

```bash
# Clone the repository
git clone https://github.com/hemachand1989/azure-payment-processing-demo.git

# Navigate to the Functions directory (IMPORTANT!)
cd azure-payment-processing-demo/PaymentProcessingFunctions
```

### Step 2: Restore NuGet Packages

```bash
# Restore all dependencies
dotnet restore
```

### Step 3: Build the Project

```bash
# Build to verify everything compiles
dotnet build
```

### Step 4: Deploy to Azure

```bash
# Now deploy (make sure you're in PaymentProcessingFunctions folder!)
func azure functionapp publish func-payment-17829
```

## 📁 Correct Directory Structure

You should be in this directory when deploying:

```
azure-payment-processing-demo/
└── PaymentProcessingFunctions/    ← YOU MUST BE HERE
    ├── host.json                   ← func looks for this
    ├── Program.cs
    ├── PaymentProcessingFunctions.csproj
    ├── local.settings.json
    ├── Functions/
    ├── Models/
    └── Services/
```

## 🔍 Verify You're in the Right Place

Run this command to check:

```bash
# This should show "host.json"
ls host.json
```

If you see "No such file or directory", you're in the wrong folder!

## 💡 Common Mistakes

### ❌ Wrong: Running from repository root
```bash
cd azure-payment-processing-demo
func azure functionapp publish func-payment-17829  # ERROR!
```

### ✅ Correct: Running from Functions directory
```bash
cd azure-payment-processing-demo/PaymentProcessingFunctions
func azure functionapp publish func-payment-17829  # SUCCESS!
```

## 🚀 Complete Deployment Script

Copy and paste this entire script:

```bash
#!/bin/bash

# Navigate to the correct directory
cd azure-payment-processing-demo/PaymentProcessingFunctions

# Restore packages
echo "Restoring NuGet packages..."
dotnet restore

# Build project
echo "Building project..."
dotnet build --configuration Release

# Deploy to Azure
echo "Deploying to Azure..."
func azure functionapp publish func-payment-17829

echo "✅ Deployment complete!"
```

## 📋 Pre-Deployment Checklist

Before deploying, make sure:

1. ✅ You ran `./setup-azure.sh` to create Azure resources
2. ✅ You have the function app name from the setup script
3. ✅ You're in the `PaymentProcessingFunctions` directory
4. ✅ You have .NET 8.0 SDK installed
5. ✅ You have Azure Functions Core Tools installed

## 🔧 Troubleshooting

### Issue: "dotnet: command not found"

Install .NET 8.0 SDK:
```bash
# Windows: Download from https://dotnet.microsoft.com/download
# macOS:
brew install dotnet@8

# Linux (Ubuntu):
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
```

### Issue: "func: command not found"

Install Azure Functions Core Tools:
```bash
# Windows: Download from https://docs.microsoft.com/azure/azure-functions/functions-run-local
# macOS:
brew tap azure/functions
brew install azure-functions-core-tools@4

# Linux (Ubuntu):
wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install azure-functions-core-tools-4
```

### Issue: Build errors about missing packages

```bash
# Clear NuGet cache and restore
dotnet nuget locals all --clear
dotnet restore
dotnet build
```

### Issue: "The system cannot find the path specified"

You're in the wrong directory! Run:
```bash
pwd  # Check current directory
cd PaymentProcessingFunctions  # Navigate to correct folder
ls host.json  # Verify host.json exists
```

## 📝 After Successful Deployment

After deployment succeeds, you'll see:

```
Deployment successful.
Remote build succeeded!

Functions in func-payment-17829:
    InitiatePayment - [httpTrigger]
        Invoke url: https://func-payment-17829.azurewebsites.net/api/payments

    PaymentEventHandler - [eventGridTrigger]

    PaymentProcessor - [serviceBusTrigger]

    PaymentValidator - [serviceBusTrigger]
```

## 🧪 Test Your Deployment

```bash
# Test the HTTP endpoint
curl -X POST https://func-payment-17829.azurewebsites.net/api/payments \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "ORD-001",
    "customerId": "CUST-001",
    "amount": 99.99,
    "currency": "USD",
    "paymentMethod": {
      "type": "CreditCard",
      "cardNumber": "4111111111111111",
      "expiryMonth": 12,
      "expiryYear": 2025,
      "cvv": "123",
      "cardholderName": "John Doe"
    }
  }'
```

Expected response:
```json
{
  "paymentId": "generated-guid",
  "status": "received",
  "message": "Payment request received and is being processed"
}
```

## 📊 Monitor Your Functions

After deployment, monitor in Azure Portal:

1. Go to https://portal.azure.com
2. Navigate to your Function App `func-payment-17829`
3. Click "Functions" to see all functions
4. Click "Monitor" to see invocations
5. Check "Logs" for real-time logging

## 🎉 Success!

Your payment processing system is now live in Azure!

Next steps:
- Test different payment scenarios
- Set up Application Insights
- Configure webhook URLs
- Monitor Service Bus queues
- Check Event Grid metrics

---

**Need help?** Check the detailed comments in each function file!
