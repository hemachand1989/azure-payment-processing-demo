# Quick Start Guide

Get up and running with the Azure Payment Processing Demo in minutes.

## What You'll Learn

This demo teaches you hands-on experience with:

✅ **Azure Service Bus Queue** - Point-to-point messaging  
✅ **Azure Service Bus Topic** - Publish-subscribe pattern  
✅ **Azure Event Grid** - Event-driven architecture  
✅ **Azure Functions** - Serverless compute with multiple triggers  
✅ **Webhooks** - HTTP callbacks to external systems  

## 5-Minute Quick Start

### Step 1: Clone the Repository

```bash
git clone https://github.com/hemachand1989/azure-payment-processing-demo.git
cd azure-payment-processing-demo
```

### Step 2: Run Setup Script

```bash
# Make script executable
chmod +x setup-azure.sh

# Run setup (takes ~5 minutes)
./setup-azure.sh
```

The script will:
- Create all Azure resources
- Configure connections
- Save configuration for you

### Step 3: Deploy Functions

```bash
cd PaymentProcessingFunctions

# Deploy to Azure
func azure functionapp publish <your-function-app-name>
```

### Step 4: Test It!

```bash
# Replace with your function app name
FUNCTION_APP="your-function-app-name"

curl -X POST https://$FUNCTION_APP.azurewebsites.net/api/payments \
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

### Step 5: Watch It Work!

1. Go to https://webhook.site and create a webhook URL
2. Update your Function App settings with the webhook URL
3. Send another payment request
4. See the webhook arrive in real-time!

## What Just Happened?

When you sent that payment request:

1. **HTTP Function** received it → sent to Service Bus **Queue**
2. **Queue Trigger** validated it → published to Service Bus **Topic**
3. **Topic Trigger** processed payment → published to Event **Grid**
4. **Event Grid Trigger** sent **webhooks** to external systems

All automatically, with retries, error handling, and monitoring!

## Understanding Each Component

### Service Bus Queue (payment-requests)

```
Customer → API → [QUEUE] → Validator (one consumer)
```

**Why Queue?**
- Only ONE function should validate each payment
- Need FIFO ordering
- Want guaranteed delivery

### Service Bus Topic (payment-status)

```
Validator → [TOPIC] → ├─ Processor
                       ├─ Notifier
                       └─ Analytics
```

**Why Topic?**
- MULTIPLE functions need the same info
- Each processes independently
- Easy to add new subscribers

### Event Grid (payment-events)

```
Processor → [EVENT GRID] → ├─ Shipping Webhook
                            ├─ Inventory Webhook
                            └─ Event Handler Function
```

**Why Event Grid?**
- Push-based delivery to webhooks
- Built-in retry logic
- Perfect for external integrations

### Azure Functions

**Trigger Types Used:**
- `HttpTrigger` - API endpoint
- `ServiceBusTrigger` (Queue) - Process queue messages
- `ServiceBusTrigger` (Topic) - Subscribe to topic
- `EventGridTrigger` - React to events

## Key Files to Explore

### 1. PaymentInitiation.cs
**HTTP Trigger → Service Bus Queue**

Learn:
- How to send messages to Service Bus Queue
- Message properties (MessageId, CorrelationId)
- Basic validation patterns

### 2. PaymentValidator.cs
**Service Bus Queue Trigger → Service Bus Topic**

Learn:
- How to consume from a queue
- Complete vs Abandon messages
- Dead letter queue handling
- Publishing to a topic

### 3. PaymentProcessor.cs
**Service Bus Topic Trigger → Event Grid**

Learn:
- How to subscribe to a topic
- Topic subscriptions and filters
- Publishing events to Event Grid
- CloudEvents schema

### 4. PaymentEventHandler.cs
**Event Grid Trigger → Webhooks**

Learn:
- Handling Event Grid events
- Sending webhooks to external systems
- HMAC signature generation
- Retry patterns

### 5. Services.cs
**Business Logic**

Learn:
- Webhook retry with exponential backoff
- HMAC signature security
- Service patterns

## Common Scenarios

### Scenario 1: Add a New Subscriber

Want to add analytics tracking?

```csharp
// 1. Create new subscription in Azure
az servicebus topic subscription create \
  --name analytics-sub \
  --topic-name payment-status

// 2. Add new function
[Function("AnalyticsTracker")]
public async Task Run(
    [ServiceBusTrigger("payment-status", "analytics-sub")]
    ServiceBusReceivedMessage message)
{
    // Your analytics logic here
}
```

That's it! Existing functions aren't affected.

### Scenario 2: Add a New Webhook

```csharp
// Just add to configuration
az functionapp config appsettings set \
  --settings "AccountingWebhookUrl=https://your-accounting-system.com/webhook"

// Update PaymentEventHandler to send it
await _webhookService.SendWebhook(
    accountingWebhookUrl,
    webhookPayload,
    "payment.completed");
```

### Scenario 3: Filter Messages

Only want to process payments over $1000?

```csharp
// Add subscription filter in Azure
az servicebus topic subscription rule create \
  --filter-type SqlFilter \
  --filter "Amount > 1000"
```

## Architecture Patterns Demonstrated

### 1. Queue Pattern (Point-to-Point)
```
One Producer → Queue → One Consumer Type
```
**Use for**: Commands, tasks, ordered processing

### 2. Topic Pattern (Pub/Sub)
```
One Producer → Topic → Many Independent Consumers
```
**Use for**: Broadcasting, multiple reactions, decoupling

### 3. Event Pattern
```
System Events → Event Grid → Webhooks + Functions
```
**Use for**: External notifications, system events, integrations

## Monitoring & Debugging

### View Messages in Transit

**Azure Portal:**
1. Service Bus namespace
2. Queues/Topics
3. See active message count
4. Peek at messages
5. Check dead letter queues

### View Function Execution

**Azure Portal:**
1. Function App
2. Functions → Select function
3. Monitor tab
4. See invocations, success rate, duration

### End-to-End Tracing

**Application Insights:**
1. Application Insights resource
2. Transaction search
3. See full request flow
4. Identify bottlenecks

## Cost Estimate

Running this demo costs approximately:

- **Service Bus Standard**: ~$10/month
- **Event Grid**: First 100K ops FREE
- **Functions Consumption**: ~$0-5/month (low traffic)
- **Storage**: ~$1/month

**Total: ~$11-16/month**

**Pro Tip**: Delete resources when not using:
```bash
az group delete --name rg-payment-demo --yes --no-wait
```

## Troubleshooting

### Functions Not Triggering?

✅ Check connection strings in Function App settings  
✅ Verify queue/topic names match  
✅ Check Function App logs for errors  

### Webhooks Not Arriving?

✅ Verify webhook URL is accessible  
✅ Check webhook service logs  
✅ Test URL with curl first  

### Messages Going to Dead Letter?

✅ Check message format (valid JSON?)  
✅ Review error messages in DLQ  
✅ Check function code for exceptions  

## Next Steps

1. **Read the Docs**
   - `docs/ARCHITECTURE.md` - Deep dive into design decisions
   - `docs/TESTING.md` - Comprehensive testing guide

2. **Modify the Code**
   - Add your own validation rules
   - Implement real payment gateway integration
   - Add database persistence

3. **Production Readiness**
   - Add Application Insights
   - Implement proper error handling
   - Set up CI/CD pipeline
   - Add monitoring alerts

## Real-World Extensions

### Add Database Persistence

```csharp
public class PaymentRepository
{
    private readonly CosmosClient _cosmos;
    
    public async Task SavePayment(PaymentRequest request)
    {
        await _cosmos.CreateItemAsync(request);
    }
}
```

### Add Real Payment Gateway

```csharp
public class StripePaymentService : IPaymentService
{
    public async Task<PaymentResult> ProcessPayment(...)
    {
        var chargeOptions = new ChargeCreateOptions { ... };
        var charge = await _stripeClient.Charges.CreateAsync(chargeOptions);
        return new PaymentResult { TransactionId = charge.Id };
    }
}
```

### Add Email Notifications

```csharp
public class SendGridNotificationService : INotificationService
{
    public async Task SendPaymentConfirmation(...)
    {
        var msg = MailHelper.CreateSingleEmail(...);
        await _sendGridClient.SendEmailAsync(msg);
    }
}
```

## Resources

- [Azure Service Bus Documentation](https://docs.microsoft.com/azure/service-bus-messaging/)
- [Azure Event Grid Documentation](https://docs.microsoft.com/azure/event-grid/)
- [Azure Functions Documentation](https://docs.microsoft.com/azure/azure-functions/)
- [Messaging Patterns](https://docs.microsoft.com/azure/architecture/patterns/category/messaging)

## Questions?

Check the detailed comments in the code - every function has extensive explanations!

## Summary

You now have a production-ready pattern for:
- ✅ Reliable message processing
- ✅ Event-driven architecture  
- ✅ Webhook integrations
- ✅ Scalable serverless compute
- ✅ Full observability

**Happy Learning! 🚀**
