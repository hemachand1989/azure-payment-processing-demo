# Azure Payment Processing Demo 💳

A comprehensive, production-ready example demonstrating **Azure Service Bus**, **Azure Event Grid**, **Azure Functions**, and **Webhooks** in a real-world payment processing scenario.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## 🎯 What You'll Learn

This hands-on demo teaches you:

- ✅ **Azure Service Bus Queue** - Reliable point-to-point messaging
- ✅ **Azure Service Bus Topic** - Publish-subscribe pattern with multiple subscribers
- ✅ **Azure Event Grid** - Event-driven architecture and webhook delivery
- ✅ **Azure Functions** - Serverless compute with 4 different trigger types
- ✅ **Webhooks** - HTTP callbacks with retry logic and security
- ✅ **Error Handling** - Dead letter queues, retries, and resilience patterns
- ✅ **Best Practices** - Idempotency, monitoring, and scalability

## 🏗️ Architecture Overview

```
┌─────────────────┐
│  Customer API   │ ──> Initiates Payment
│ (HTTP Trigger)  │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────────────────┐
│     Azure Service Bus QUEUE (payment-requests)      │
│  • Reliable delivery  • FIFO processing  • DLQ      │
└────────┬────────────────────────────────────────────┘
         │
         ▼
┌─────────────────┐      ┌──────────────────┐
│ Payment         │──>   │  Fraud Detection │
│ Validator       │      │  Service         │
└────────┬────────┘      └──────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────┐
│     Azure Service Bus TOPIC (payment-status)        │
│  • Pub/Sub  • Multiple subscribers  • Filters      │
└────────┬────────────────────────────────────────────┘
         │
         ├──> Payment Processor
         ├──> Notification Service
         └──> Analytics Service
         
         ▼
┌─────────────────────────────────────────────────────┐
│          Azure Event Grid (payment-events)          │
│  • Webhook delivery  • Built-in retry  • Push      │
└────────┬────────────────────────────────────────────┘
         │
         ├──> Webhook (Shipping System)
         ├──> Webhook (Inventory System)
         └──> Event Handler Function
```

## 🚀 Quick Start (5 Minutes)

### 1. Clone the Repository

```bash
git clone https://github.com/hemachand1989/azure-payment-processing-demo.git
cd azure-payment-processing-demo
```

### 2. Run Setup Script

```bash
chmod +x setup-azure.sh
./setup-azure.sh
```

The script creates:
- Service Bus namespace with queue and topic
- Event Grid topic
- Storage account
- Function App

### 3. Deploy Functions

```bash
cd PaymentProcessingFunctions
func azure functionapp publish <your-function-app-name>
```

### 4. Test the System

```bash
curl -X POST https://<your-function-app>.azurewebsites.net/api/payments \
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
      "cvv": "123"
    }
  }'
```

See [QUICKSTART.md](QUICKSTART.md) for detailed instructions.

## 📁 Project Structure

```
azure-payment-processing-demo/
├── PaymentProcessingFunctions/
│   ├── Functions/
│   │   ├── PaymentInitiation.cs       # HTTP → Service Bus Queue
│   │   ├── PaymentValidator.cs        # Queue → Topic
│   │   ├── PaymentProcessor.cs        # Topic → Event Grid
│   │   └── PaymentEventHandler.cs     # Event Grid → Webhooks
│   ├── Models/
│   │   ├── PaymentRequest.cs          # Request DTOs
│   │   ├── PaymentStatus.cs           # Status DTOs
│   │   └── PaymentEvent.cs            # Event models
│   ├── Services/
│   │   └── Services.cs                # Business logic & webhooks
│   └── Program.cs                     # DI configuration
├── docs/
│   ├── ARCHITECTURE.md                # Deep dive explanations
│   └── TESTING.md                     # Testing guide
├── QUICKSTART.md                      # 5-minute setup
├── setup-azure.sh                     # Automated Azure setup
└── README.md                          # This file
```

## 🎓 Learning Path

### Beginner: Start Here

1. **Read** [QUICKSTART.md](QUICKSTART.md) - Get running in 5 minutes
2. **Explore** `PaymentInitiation.cs` - See HTTP trigger and Queue
3. **Run** a test payment and watch the logs

### Intermediate: Understand the Flow

1. **Read** `PaymentValidator.cs` - Queue trigger and Topic publishing
2. **Read** `PaymentProcessor.cs` - Topic trigger and Event Grid
3. **Test** different scenarios (validation failure, fraud detection)

### Advanced: Architecture Deep Dive

1. **Read** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) - Design decisions
2. **Modify** the code to add your own features
3. **Deploy** to production with monitoring

## 📚 Key Concepts Explained

### When to Use Queue vs Topic

**Queue** (Point-to-Point):
```
Producer → [Queue] → Single Consumer Type
```
- Use for: Commands, ordered processing, work distribution
- Example: Payment requests that must be processed once

**Topic** (Pub/Sub):
```
Producer → [Topic] → Multiple Independent Consumers
```
- Use for: Broadcasting, parallel processing, decoupling
- Example: Payment status that multiple systems need

### When to Use Service Bus vs Event Grid

**Service Bus**:
- Internal messaging
- Need FIFO ordering
- Rich features (sessions, transactions)
- Pull-based (consumer controls pace)

**Event Grid**:
- External notifications
- Webhook delivery
- Massive scale (millions/sec)
- Push-based (immediate delivery)

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed comparison.

## 🔍 What Makes This Demo Special

### ✨ Production-Ready Code

- ✅ Proper error handling and retries
- ✅ Dead letter queue management
- ✅ Idempotency patterns
- ✅ HMAC signature security for webhooks
- ✅ Structured logging
- ✅ Dependency injection

### 📖 Extensive Documentation

Every file has:
- Detailed comments explaining "why" not just "what"
- Real-world use cases
- Best practices
- Common pitfalls to avoid

### 🎯 Hands-On Learning

- Working code you can run immediately
- Test scenarios for different use cases
- Monitoring and debugging guidance
- Scalability considerations

## 🧪 Testing Different Scenarios

### Successful Payment
```bash
curl -X POST http://localhost:7071/api/payments \
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
      "cvv": "123"
    }
  }'
```

### Validation Failure
```bash
# Invalid card number
curl -X POST http://localhost:7071/api/payments \
  -d '{"orderId": "ORD-002", "amount": -50, "paymentMethod": {"cardNumber": "123"}}'
```

### High Fraud Score (triggers review)
```bash
# Large amount triggers fraud detection
curl -X POST http://localhost:7071/api/payments \
  -d '{"orderId": "ORD-003", "amount": 9999.99, ...}'
```

See [docs/TESTING.md](docs/TESTING.md) for comprehensive testing guide.

## 📊 Monitoring & Observability

### View in Azure Portal

- **Service Bus**: See message counts, throughput, dead letters
- **Event Grid**: Monitor event delivery success rate
- **Functions**: View invocations, duration, errors
- **Application Insights**: End-to-end tracing

### Query Application Insights

```kusto
// Find all payment requests
requests
| where name == "InitiatePayment"
| project timestamp, paymentId=customDimensions.PaymentId

// Trace full payment flow
traces
| where message contains "payment-id-here"
| order by timestamp asc
```

## 💰 Cost Estimate

Running this demo:
- Service Bus Standard: ~$10/month
- Event Grid: First 100K ops FREE
- Functions Consumption: ~$0-5/month
- Storage: ~$1/month

**Total: ~$11-16/month**

Delete when not in use:
```bash
az group delete --name rg-payment-demo --yes --no-wait
```

## 🛠️ Prerequisites

- Azure subscription
- Azure CLI installed
- .NET 8.0 SDK
- Azure Functions Core Tools v4
- VS Code or Visual Studio (optional)

## 📝 Common Use Cases

This pattern applies to many scenarios:

- ✅ **E-commerce**: Order processing pipelines
- ✅ **IoT**: Device telemetry processing
- ✅ **Finance**: Transaction processing
- ✅ **Healthcare**: Patient data workflows
- ✅ **Logistics**: Shipment tracking
- ✅ **SaaS**: Multi-tenant event processing

## 🤝 Contributing

Found an issue or have a suggestion? Feel free to:
- Open an issue
- Submit a pull request
- Star the repo if you find it helpful!

## 📖 Additional Resources

- [Azure Service Bus Documentation](https://docs.microsoft.com/azure/service-bus-messaging/)
- [Azure Event Grid Documentation](https://docs.microsoft.com/azure/event-grid/)
- [Azure Functions Documentation](https://docs.microsoft.com/azure/azure-functions/)
- [Messaging Patterns](https://docs.microsoft.com/azure/architecture/patterns/category/messaging)

## 📄 License

MIT License - feel free to use this in your own projects!

## 🎉 What's Next?

1. ⭐ **Star this repo** if you found it helpful
2. 📖 **Read the code** - every line is documented
3. 🚀 **Deploy it** - see it running in Azure
4. 🔧 **Modify it** - make it your own
5. 📢 **Share it** - help others learn

---

**Built with ❤️ to help you master Azure messaging services**

Questions? Check the extensive comments in the code files!
