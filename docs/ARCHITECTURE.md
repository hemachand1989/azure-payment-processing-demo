# Architecture Deep Dive

This document explains the architectural decisions and patterns used in the payment processing system.

## Table of Contents
1. [Service Bus Queue vs Topic](#service-bus-queue-vs-topic)
2. [Service Bus vs Event Grid](#service-bus-vs-event-grid)
3. [Messages vs Events](#messages-vs-events)
4. [Webhooks Implementation](#webhooks-implementation)
5. [Error Handling & Resilience](#error-handling--resilience)
6. [Scaling Considerations](#scaling-considerations)

## Service Bus Queue vs Topic

### When to Use a Queue

**Use Case: Payment Request Processing**

```
Customer → API → [Queue] → Single Processor
```

**Characteristics:**
- **Point-to-Point**: One message, one consumer
- **Competitive Consumers**: Multiple instances compete for messages
- **Load Leveling**: Absorbs traffic spikes
- **FIFO Ordering**: Messages processed in order (with sessions)
- **Once-Only Processing**: Each message processed exactly once

**Why we used Queue for payment-requests:**
1. Only ONE function should validate each payment
2. Need ordered processing (FIFO)
3. Want to decouple API response from processing
4. Need guaranteed delivery with retry

**Example Code Pattern:**
```csharp
// Producer (API)
await sender.SendMessageAsync(message);

// Consumer (Validator)
[ServiceBusTrigger("payment-requests", Connection = "ServiceBusConnection")]
public async Task ValidatePayment(ServiceBusReceivedMessage message)
{
    // Process message
    await messageActions.CompleteMessageAsync(message);
}
```

### When to Use a Topic

**Use Case: Payment Status Broadcasting**

```
Validator → [Topic] → ├─ Processor
                       ├─ Notifier
                       └─ Analytics
```

**Characteristics:**
- **Publish-Subscribe**: One message, many consumers
- **Independent Processing**: Each subscriber processes independently
- **Filtering**: Subscribers can filter messages
- **Multiple Copies**: Each subscription gets its own copy
- **Decoupled Systems**: Subscribers don't know about each other

**Why we used Topic for payment-status:**
1. MULTIPLE functions need the same information
2. Each processes independently (parallel)
3. Adding new subscribers doesn't affect existing ones
4. Different subscribers need different processing logic

**Example Code Pattern:**
```csharp
// Producer (Validator)
await topicSender.SendMessageAsync(message);

// Consumer 1 (Processor)
[ServiceBusTrigger("payment-status", "processor-sub", Connection = "...")]
public async Task ProcessPayment(ServiceBusReceivedMessage message) { }

// Consumer 2 (Notifier) - Runs simultaneously
[ServiceBusTrigger("payment-status", "notification-sub", Connection = "...")]
public async Task SendNotification(ServiceBusReceivedMessage message) { }
```

### Subscription Filters

Subscriptions can filter messages without retrieving them:

```csharp
// SQL Filter: Only get completed payments
new SqlRuleFilter("Status = 'Completed'")

// Correlation Filter: Only get high-value payments
new CorrelationRuleFilter { 
    Properties = { ["Amount"] = ">1000" }
}
```

## Service Bus vs Event Grid

### Azure Service Bus

**Best For:**
- Enterprise messaging
- Internal application communication
- Reliable, ordered delivery needed
- Rich messaging features (sessions, transactions, duplicate detection)
- Pull-based consumption (consumer controls pace)

**Features:**
- Dead letter queues
- Message sessions (FIFO guarantees)
- Scheduled messages
- Message deferral
- Transaction support
- Duplicate detection

**Use Cases:**
- Order processing pipelines
- Task queues
- Command processing
- Workflow orchestration
- Internal microservices communication

**Cost Model:**
- Based on operations (messages sent/received)
- Premium tier for higher throughput
- ~$10-50/month for moderate usage

### Azure Event Grid

**Best For:**
- Event-driven architectures
- React to state changes
- Webhook notifications
- Massive scale (millions of events/sec)
- Push-based delivery

**Features:**
- Built-in retry with exponential backoff
- Dead-lettering for events
- Event filtering
- Multiple endpoint types (webhooks, functions, queues)
- CloudEvents schema support
- Advanced filtering (operators, arrays)

**Use Cases:**
- System-wide event broadcasting
- Webhook delivery to external systems
- Cross-service communication
- IoT telemetry
- Storage/Blob events
- Custom application events

**Cost Model:**
- First 100,000 operations/month: FREE
- After that: $0.60 per million operations
- Very cost-effective at scale

### Architecture Decision Matrix

| Criterion | Service Bus | Event Grid |
|-----------|-------------|------------|
| **Message Size** | Up to 1MB (256KB standard) | Up to 1MB |
| **Throughput** | Thousands/sec | Millions/sec |
| **Delivery** | Pull (polling) | Push (immediate) |
| **Ordering** | Yes (with sessions) | No |
| **Filtering** | SQL, Correlation | Advanced JSON filtering |
| **Webhooks** | No native support | Built-in |
| **Dead Letter** | Yes | Yes |
| **Transactions** | Yes | No |
| **Best For** | Reliable messaging | Event distribution |

### Why We Used Both

```
[HTTP] → [Service Bus Queue] → [Validator] → [Service Bus Topic] → [Processor]
                                                                         ↓
                                                                   [Event Grid] → Webhooks
```

**Service Bus Queue/Topic:**
- Internal processing pipeline
- Need reliable, ordered processing
- Rich messaging features (DLQ, sessions)
- Pull-based for controlled processing

**Event Grid:**
- External system notifications
- Webhook delivery
- Don't need ordering
- Push-based for immediate delivery
- Cost-effective at scale

## Messages vs Events

### Messages (Service Bus)

**Characteristics:**
- **Command-Oriented**: "Do this"
- **Producer Cares**: Expects specific action
- **Contains Intent**: Business logic embedded
- **Present/Future Tense**: "Process payment", "Send email"

**Example:**
```json
{
  "command": "ProcessPayment",
  "paymentId": "123",
  "amount": 99.99,
  "action": "charge"
}
```

**Producer Mindset:** "I need you to process this payment"

### Events (Event Grid)

**Characteristics:**
- **Fact-Oriented**: "This happened"
- **Producer Doesn't Care**: Doesn't know/care who listens
- **Historical Record**: Statement about past
- **Past Tense**: "Payment completed", "Order shipped"

**Example:**
```json
{
  "eventType": "PaymentCompleted",
  "paymentId": "123",
  "occurredAt": "2025-09-29T10:00:00Z",
  "data": { ... }
}
```

**Producer Mindset:** "Just letting everyone know this happened"

### Practical Difference in Code

**Message (Command):**
```csharp
// Producer knows there's a processor
await queueClient.SendMessageAsync(new ProcessPaymentCommand {
    PaymentId = "123",
    Action = "Charge",
    ExpectedProcessor = "PaymentGateway"
});

// Consumer MUST process this
[ServiceBusTrigger("payment-commands")]
public async Task ProcessPayment(ProcessPaymentCommand cmd) {
    // MUST charge the payment
    await paymentGateway.Charge(cmd.PaymentId);
}
```

**Event (Notification):**
```csharp
// Producer doesn't know who's listening
await eventGrid.PublishEventAsync(new PaymentCompletedEvent {
    PaymentId = "123",
    OccurredAt = DateTime.UtcNow
});

// ANY number of consumers can react
[EventGridTrigger]
public async Task OnPaymentCompleted(PaymentCompletedEvent evt) {
    // Optional reaction - event already happened
    await shipping.PrepareOrder(evt.PaymentId);
}
```

## Webhooks Implementation

### What Are Webhooks?

Webhooks are "reverse APIs" - instead of you polling for updates, the service calls you when something happens.

```
Traditional API:        Webhook:
You → Poll → Service   Service → Push → You
```

### Webhook Security (HMAC Signatures)

**Problem:** How does the receiver know the webhook is authentic?

**Solution:** HMAC signatures

```csharp
// Sender (our code)
string payload = JsonSerializer.Serialize(data);
byte[] secret = Encoding.UTF8.GetBytes("shared-secret");
using var hmac = new HMACSHA256(secret);
string signature = hmac.ComputeHash(payload);

// Include in header or payload
headers.Add("X-Signature", signature);
```

```csharp
// Receiver (external system)
string receivedPayload = await request.Content.ReadAsStringAsync();
string receivedSignature = request.Headers["X-Signature"];

// Compute signature with their copy of secret
byte[] secret = Encoding.UTF8.GetBytes("shared-secret");
using var hmac = new HMACSHA256(secret);
string computedSignature = hmac.ComputeHash(receivedPayload);

if (computedSignature == receivedSignature) {
    // Authentic webhook
} else {
    // Reject - possible forgery
}
```

### Webhook Retry Strategy

```csharp
for (int attempt = 1; attempt <= MaxRetries; attempt++)
{
    try {
        var response = await httpClient.PostAsync(url, content);
        
        if (response.IsSuccessStatusCode)
            return true;
        
        // Don't retry client errors (4xx)
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
            return false;
    }
    catch (Exception) {
        // Exponential backoff: 2s, 4s, 8s
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }
}
```

### Webhook Best Practices

1. **Idempotency**: Include unique ID so receiver can deduplicate
2. **Signatures**: Always sign webhooks with HMAC
3. **Retries**: Implement exponential backoff
4. **Timeouts**: Don't wait forever (10s timeout)
5. **Logging**: Log all webhook attempts for debugging
6. **Versioning**: Include API version for compatibility
7. **Dead Letter**: Store failed webhooks for manual retry

## Error Handling & Resilience

### Service Bus Message Lifecycle

```
[Queue] → Lock Message → Process → Complete ✓
                    ↓
                  Fail → Abandon → Back to Queue
                                        ↓
                                   Retry (up to MaxDeliveryCount)
                                        ↓
                                   Dead Letter Queue
```

### Retry Patterns

**Transient Failures** (network blips):
```csharp
await messageActions.AbandonMessageAsync(message);
// Message goes back to queue, will be retried
```

**Permanent Failures** (bad data):
```csharp
await messageActions.DeadLetterMessageAsync(
    message,
    "InvalidData",
    "Card number format invalid"
);
// Message moves to DLQ, won't be retried
```

### Circuit Breaker Pattern

```csharp
if (consecutiveFailures > 5) {
    // Stop calling failing service
    // Wait for cooldown period
    await Task.Delay(TimeSpan.FromMinutes(5));
    consecutiveFailures = 0;
}
```

## Scaling Considerations

### Automatic Scaling

Azure Functions automatically scales based on:
- **Queue Depth**: More messages = more instances
- **Processing Time**: Slow processing = more instances
- **Max Concurrent**: Configured limits

### Configuration for Scale

```json
{
  "extensions": {
    "serviceBus": {
      "prefetchCount": 100,          // Fetch messages in batches
      "maxConcurrentCalls": 32,      // Process 32 messages simultaneously
      "maxAutoRenewDuration": "00:05:00"
    }
  }
}
```

### Scaling Patterns

**Scale Out (Horizontal):**
- Add more Function instances
- Each processes messages independently
- Service Bus distributes load

**Scale Up (Vertical):**
- Use Premium Functions plan
- More CPU/memory per instance
- Better for CPU-intensive tasks

### Cost Optimization

1. **Batching**: Process multiple messages together
2. **Right-Size**: Don't over-provision
3. **Consumption Plan**: Pay only for execution time
4. **Reserved Instances**: Discount for predictable workloads

---

This architecture provides:
✅ **Reliability**: Messages aren't lost  
✅ **Scalability**: Automatically handles load  
✅ **Resilience**: Retries and error handling  
✅ **Flexibility**: Easy to add new components  
✅ **Observability**: Full logging and tracing
