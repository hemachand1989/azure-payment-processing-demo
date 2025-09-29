using Azure.Messaging.EventGrid;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentProcessingFunctions.Models;
using PaymentProcessingFunctions.Services;
using System.Text.Json;

namespace PaymentProcessingFunctions.Functions;

/*
 * PAYMENT PROCESSOR FUNCTION - SERVICE BUS TOPIC TRIGGER
 * 
 * This function demonstrates the SUBSCRIBER side of the Topic pattern.
 * It's one of potentially many subscribers to the "payment-status" topic.
 * 
 * TOPIC SUBSCRIPTION PATTERN:
 * 
 * When PaymentValidator publishes to the topic, multiple functions receive the message:
 * 1. PaymentProcessor (this function) - Processes the actual payment
 * 2. NotificationService - Sends customer notifications
 * 3. AnalyticsService - Records metrics
 * 
 * KEY CONCEPTS:
 * 
 * SUBSCRIPTIONS:
 * - Each subscriber has its own subscription to the topic
 * - Subscription name: "payment-processor-sub"
 * - Messages are delivered independently to each subscription
 * - Each subscription maintains its own delivery state
 * 
 * FILTERS:
 * - Subscriptions can filter messages without retrieving them
 * - Example: Only process "Validated" status messages
 * - Filters are evaluated in Azure, not in your code
 * - Reduces unnecessary function invocations
 * 
 * SCALING:
 * - Each subscription scales independently
 * - If this function is slow, it doesn't affect other subscribers
 * - Azure Functions can create multiple instances to process messages in parallel
 * 
 * This function also demonstrates:
 * - Processing payments with external gateway
 * - Publishing events to Event Grid
 * - Error handling and retries
 */

public class PaymentProcessor
{
    private readonly ILogger<PaymentProcessor> _logger;
    private readonly IPaymentService _paymentService;
    private readonly EventGridPublisherClient _eventGridPublisher;

    public PaymentProcessor(
        ILogger<PaymentProcessor> logger,
        IPaymentService paymentService,
        EventGridPublisherClient eventGridPublisher)
    {
        _logger = logger;
        _paymentService = paymentService;
        _eventGridPublisher = eventGridPublisher;
    }

    /*
     * SERVICE BUS TOPIC SUBSCRIPTION TRIGGER
     * 
     * Key differences from Queue trigger:
     * - topicName: The topic to subscribe to
     * - subscriptionName: This function's specific subscription
     * - Multiple functions can subscribe to the same topic with different subscriptions
     * 
     * Message Flow:
     * 1. PaymentValidator publishes ONE message to "payment-status" topic
     * 2. Service Bus creates a COPY for each subscription
     * 3. This function processes the message from "payment-processor-sub"
     * 4. Other functions process from their own subscriptions simultaneously
     */
    [Function("PaymentProcessor")]
    public async Task Run(
        [ServiceBusTrigger(
            "payment-status",
            "payment-processor-sub",
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        _logger.LogInformation(
            "Payment processor triggered for MessageId: {MessageId}",
            message.MessageId);

        try
        {
            // STEP 1: Deserialize the payment status message
            var messageBody = message.Body.ToString();
            var paymentStatus = JsonSerializer.Deserialize<PaymentStatus>(messageBody);

            if (paymentStatus == null)
            {
                await messageActions.DeadLetterMessageAsync(
                    message,
                    "DeserializationFailed",
                    "Could not deserialize PaymentStatus");
                return;
            }

            _logger.LogInformation(
                "Processing payment {PaymentId} with status {Status}",
                paymentStatus.PaymentId,
                paymentStatus.Status);

            // STEP 2: Only process validated payments
            // Payments that failed validation or are flagged for fraud review are skipped
            if (paymentStatus.Status != PaymentStatusType.Validated)
            {
                _logger.LogInformation(
                    "Skipping payment {PaymentId} - Status is {Status}, not Validated",
                    paymentStatus.PaymentId,
                    paymentStatus.Status);

                await messageActions.CompleteMessageAsync(message);
                return;
            }

            // STEP 3: Check if payment is flagged for fraud
            if (paymentStatus.FraudFlagged)
            {
                _logger.LogWarning(
                    "Payment {PaymentId} flagged for fraud review (score: {Score})",
                    paymentStatus.PaymentId,
                    paymentStatus.FraudScore);

                // In a real system, you might:
                // - Send to manual review queue
                // - Hold payment for 24 hours
                // - Request additional verification
                await messageActions.CompleteMessageAsync(message);
                return;
            }

            // STEP 4: Process the payment with payment gateway
            // This simulates calling Stripe, PayPal, Square, etc.
            _logger.LogInformation(
                "Charging payment gateway for {Amount} {Currency}",
                paymentStatus.Amount,
                paymentStatus.Currency);

            var processingResult = await _paymentService.ProcessPayment(
                paymentStatus.PaymentId,
                paymentStatus.Amount,
                paymentStatus.Currency);

            // STEP 5: Create and publish Event Grid event
            // This is where we transition from Service Bus to Event Grid
            var paymentEvent = new PaymentEvent
            {
                EventType = processingResult.Success ? "PaymentCompleted" : "PaymentFailed",
                PaymentId = paymentStatus.PaymentId,
                OrderId = paymentStatus.OrderId,
                CustomerId = paymentStatus.CustomerId,
                Amount = paymentStatus.Amount,
                Currency = paymentStatus.Currency,
                Status = processingResult.Success ? "Completed" : "Failed",
                TransactionId = processingResult.TransactionId,
                OccurredAt = DateTime.UtcNow,
                Data = new Dictionary<string, object>
                {
                    { "ProcessedAt", DateTime.UtcNow },
                    { "GatewayResponse", processingResult.ResponseCode },
                    { "FraudScore", paymentStatus.FraudScore }
                }
            };

            await PublishToEventGrid(paymentEvent);

            // STEP 6: Complete the message
            await messageActions.CompleteMessageAsync(message);

            _logger.LogInformation(
                "Payment {PaymentId} processed successfully: {Success}",
                paymentStatus.PaymentId,
                processingResult.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing payment for MessageId: {MessageId}",
                message.MessageId);

            // Implement retry logic
            if (message.DeliveryCount >= 3)
            {
                await messageActions.DeadLetterMessageAsync(
                    message,
                    "ProcessingFailed",
                    ex.Message);
            }
            else
            {
                // Abandon with a delay for exponential backoff
                // In production, use built-in Service Bus retry policies
                await messageActions.AbandonMessageAsync(message);
            }
        }
    }

    /*
     * PUBLISHING TO EVENT GRID
     * 
     * CRITICAL CONCEPT: Why Event Grid instead of Service Bus here?
     * 
     * SERVICE BUS (what we just consumed from):
     * - Enterprise messaging within your application
     * - Reliable, ordered delivery
     * - Rich messaging features (sessions, transactions)
     * - Pull-based: Consumer controls pace
     * - Best for: Commands, tasks, internal workflows
     * 
     * EVENT GRID (what we're publishing to now):
     * - Event routing and distribution
     * - Push-based: Events pushed to subscribers immediately
     * - Built-in support for webhooks to external systems
     * - Massive scale (millions of events per second)
     * - Built-in retry and dead-lettering for webhooks
     * - Best for: System events, webhooks, integrations
     * 
     * Why use Event Grid here?
     * 1. Need to notify EXTERNAL systems (shipping, inventory, CRM)
     * 2. These systems prefer webhooks (HTTP callbacks)
     * 3. Event Grid handles webhook delivery, retries, and failures
     * 4. Event Grid provides event schema validation
     * 5. Easy integration with Azure services and external systems
     * 
     * Event Grid Schema:
     * - EventId: Unique identifier for deduplication
     * - EventType: Type of event for filtering/routing
     * - Subject: Additional context for filtering
     * - EventTime: When the event occurred
     * - Data: The actual event payload
     * - DataVersion: Schema version for compatibility
     */
    private async Task PublishToEventGrid(PaymentEvent paymentEvent)
    {
        // Create Event Grid event in CloudEvents schema
        // CloudEvents is a standardized event format (https://cloudevents.io/)
        var eventGridEvent = new EventGridEvent(
            subject: $"payments/{paymentEvent.PaymentId}",
            eventType: paymentEvent.EventType,
            dataVersion: "1.0",
            data: new
            {
                paymentEvent.PaymentId,
                paymentEvent.OrderId,
                paymentEvent.CustomerId,
                paymentEvent.Amount,
                paymentEvent.Currency,
                paymentEvent.Status,
                paymentEvent.TransactionId,
                paymentEvent.OccurredAt,
                paymentEvent.Data
            })
        {
            Id = paymentEvent.EventId,
            EventTime = paymentEvent.OccurredAt
        };

        // Publish to Event Grid
        // Event Grid will then:
        // 1. Validate the event schema
        // 2. Route to all subscribers based on filters
        // 3. Deliver to webhooks with retry logic
        // 4. Handle failures and dead-lettering
        await _eventGridPublisher.SendEventAsync(eventGridEvent);

        _logger.LogInformation(
            "Event published to Event Grid: {EventType} for PaymentId: {PaymentId}",
            paymentEvent.EventType,
            paymentEvent.PaymentId);
    }
}
