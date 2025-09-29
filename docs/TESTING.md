# Testing the Payment Processing System

This guide walks you through testing each component of the system.

## Prerequisites

1. Azure resources deployed (see main README)
2. Function app running locally or in Azure
3. [Postman](https://www.postman.com/) or curl for API testing
4. [webhook.site](https://webhook.site) for webhook testing

## Test Scenario: Complete Payment Flow

### Step 1: Set Up Webhook Endpoints

1. Go to https://webhook.site
2. You'll get a unique URL like `https://webhook.site/#!/abc-123-def`
3. Copy this URL
4. Add to your `local.settings.json`:
```json
{
  "Values": {
    "ShippingWebhookUrl": "https://webhook.site/your-unique-id",
    "InventoryWebhookUrl": "https://webhook.site/your-unique-id"
  }
}
```

### Step 2: Initiate a Payment (HTTP Trigger)

```bash
curl -X POST http://localhost:7071/api/payments \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "ORD-12345",
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
    },
    "billingAddress": {
      "street": "123 Main St",
      "city": "New York",
      "state": "NY",
      "postalCode": "10001",
      "country": "USA"
    }
  }'
```

**Expected Response:**
```json
{
  "paymentId": "generated-guid",
  "status": "received",
  "message": "Payment request received and is being processed",
  "timestamp": "2025-09-29T10:00:00Z"
}
```

**What Happens:**
1. HTTP function receives request
2. Validates basic input
3. Sends message to Service Bus Queue "payment-requests"
4. Returns immediate acknowledgment

### Step 3: Monitor Service Bus Queue

**Azure Portal:**
1. Go to your Service Bus namespace
2. Click "Queues" → "payment-requests"
3. You should see:
   - Active Message Count: 1 (briefly, then 0 after processing)
   - Incoming Messages chart shows activity

**What Happens:**
1. PaymentValidator function is triggered by queue message
2. Validates payment details
3. Runs fraud detection
4. Publishes to Service Bus Topic "payment-status"

### Step 4: Monitor Service Bus Topic

**Azure Portal:**
1. Go to your Service Bus namespace
2. Click "Topics" → "payment-status"
3. Click "Subscriptions"
4. Check each subscription:
   - payment-processor-sub
   - notification-sub
   - analytics-sub
5. See message counts for each

**What Happens:**
1. Message published to topic
2. Each subscription gets a copy
3. Multiple functions process independently:
   - PaymentProcessor processes the payment
   - NotificationService sends email (if implemented)
   - AnalyticsService records metrics (if implemented)

### Step 5: Check Event Grid Events

**Azure Portal:**
1. Go to your Event Grid Topic
2. Click "Event Subscriptions"
3. Click "Metrics" to see events published
4. Should see "Publish Succeeded" metric increase

**What Happens:**
1. PaymentProcessor publishes "PaymentCompleted" or "PaymentFailed" event
2. Event Grid routes to subscribers:
   - PaymentEventHandler function
   - Any configured webhooks

### Step 6: Verify Webhook Delivery

**webhook.site:**
1. Go back to your webhook.site tab
2. You should see POST requests arrive
3. Examine the payload:

```json
{
  "id": "webhook-event-id",
  "type": "payment.completed",
  "createdAt": "2025-09-29T10:00:05Z",
  "data": {
    "eventId": "event-guid",
    "eventType": "PaymentCompleted",
    "paymentId": "payment-guid",
    "orderId": "ORD-12345",
    "customerId": "CUST-001",
    "amount": 99.99,
    "currency": "USD",
    "status": "Completed",
    "transactionId": "TXN-xyz",
    "occurredAt": "2025-09-29T10:00:04Z"
  },
  "signature": "hmac-signature",
  "apiVersion": "2025-01-01"
}
```

## Testing Different Scenarios

### Scenario 1: Validation Failure

```bash
curl -X POST http://localhost:7071/api/payments \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "ORD-12346",
    "customerId": "CUST-001",
    "amount": -50,
    "currency": "USD",
    "paymentMethod": {
      "type": "CreditCard",
      "cardNumber": "123",
      "expiryMonth": 1,
      "expiryYear": 2020,
      "cvv": "1"
    }
  }'
```

### Scenario 2: High Fraud Score

```bash
curl -X POST http://localhost:7071/api/payments \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "ORD-12347",
    "customerId": "CUST-NEW",
    "amount": 9999.99,
    "currency": "USD",
    "paymentMethod": {
      "type": "CreditCard",
      "cardNumber": "4111111111111111",
      "expiryMonth": 12,
      "expiryYear": 2025,
      "cvv": "123"
    }
  }'
```

## Monitoring & Debugging

### View Dead Letter Queue Messages

```bash
# Azure CLI
az servicebus queue show \
  --name payment-requests \
  --namespace-name your-namespace \
  --resource-group your-rg \
  --query deadLetterMessageCount
```

### Application Insights Query Examples

```kusto
// Find all payment requests
requests
| where name == "InitiatePayment"
| project timestamp, paymentId=customDimensions.PaymentId, resultCode

// Trace end-to-end flow
traces
| where message contains "payment-guid"
| order by timestamp asc

// Find failed webhooks
traces
| where message contains "Webhook delivery failed"
| project timestamp, message, severityLevel
```

## Performance Testing

### Load Test with Artillery

```yaml
# artillery-load-test.yml
config:
  target: "http://localhost:7071"
  phases:
    - duration: 60
      arrivalRate: 10
scenarios:
  - name: "Payment Flow"
    flow:
      - post:
          url: "/api/payments"
          json:
            orderId: "ORD-{{ $randomString() }}"
            customerId: "CUST-001"
            amount: 99.99
            currency: "USD"
            paymentMethod:
              type: "CreditCard"
              cardNumber: "4111111111111111"
              expiryMonth: 12
              expiryYear: 2025
              cvv: "123"
```

Run: `artillery run artillery-load-test.yml`

## Common Issues & Solutions

### Issue: Function Not Triggering
- Check connection string in configuration
- Verify queue/topic names match
- Check Function App logs for errors

### Issue: Webhooks Not Delivered
- Verify webhook URL is accessible
- Check firewall rules
- Review webhook service logs

### Issue: Messages in Dead Letter Queue
- Check message content
- Review error messages
- Verify deserialization works

### Issue: High Latency
- Check Service Bus metrics
- Review Function scaling settings
- Monitor Application Insights performance
