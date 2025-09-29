using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentProcessingFunctions.Models;
using PaymentProcessingFunctions.Services;
using System.Text.Json;

namespace PaymentProcessingFunctions.Functions;

/*
 * PAYMENT EVENT HANDLER - EVENT GRID TRIGGER
 * 
 * This function demonstrates consuming events from Azure Event Grid.
 * It's triggered automatically when events are published to Event Grid.
 * 
 * EVENT GRID TRIGGER CONCEPTS:
 * 
 * PUSH MODEL:
 * - Unlike Service Bus (pull), Event Grid PUSHES events to subscribers
 * - Function is invoked immediately when event arrives
 * - No polling required
 * 
 * EVENT SUBSCRIPTION:
 * - You create an Event Grid subscription pointing to this function
 * - Subscription can filter events by type, subject, etc.
 * - Filters are applied before delivery (saves invocations)
 * 
 * BUILT-IN FEATURES:
 * - Automatic retry with exponential backoff
 * - Dead-lettering for failed deliveries
 * - At-least-once delivery guarantee
 * - Event schema validation
 * 
 * WHEN TO USE EVENT GRID TRIGGERS:
 * - React to system-wide events
 * - Handle events from multiple sources
 * - Process events from other Azure services (Storage, IoT Hub, etc.)
 * - Internal event processing alongside webhooks
 * 
 * This function handles payment events and:
 * 1. Sends webhooks to external systems
 * 2. Updates internal databases
 * 3. Triggers follow-up workflows
 */

public class PaymentEventHandler
{
    private readonly ILogger<PaymentEventHandler> _logger;
    private readonly IWebhookService _webhookService;

    public PaymentEventHandler(
        ILogger<PaymentEventHandler> logger,
        IWebhookService webhookService)
    {
        _logger = logger;
        _webhookService = webhookService;
    }

    /*
     * EVENT GRID TRIGGER FUNCTION
     * 
     * The EventGridEvent parameter contains:
     * - Id: Unique event identifier
     * - EventType: Type of event (PaymentCompleted, PaymentFailed)
     * - Subject: Hierarchical identifier (payments/{paymentId})
     * - EventTime: When the event occurred
     * - Data: The event payload
     * - DataVersion: Schema version
     * 
     * Event Grid guarantees:
     * - At-least-once delivery
     * - Durable delivery with retries
     * - Event ordering within same subject (optional)
     */
    [Function("PaymentEventHandler")]
    public async Task Run(
        [EventGridTrigger] EventGridEvent eventGridEvent)
    {
        _logger.LogInformation(
            "Processing Event Grid event - Type: {EventType}, Subject: {Subject}, Id: {Id}",
            eventGridEvent.EventType,
            eventGridEvent.Subject,
            eventGridEvent.Id);

        try
        {
            // STEP 1: Deserialize the event data
            // Event Grid wraps your custom data in the Data property
            var paymentEventData = JsonSerializer.Deserialize<PaymentEventData>(
                eventGridEvent.Data.ToString()!);

            if (paymentEventData == null)
            {
                _logger.LogError("Failed to deserialize payment event data");
                throw new InvalidOperationException("Invalid event data");
            }

            _logger.LogInformation(
                "Processing {EventType} for Payment {PaymentId}, Order {OrderId}",
                eventGridEvent.EventType,
                paymentEventData.PaymentId,
                paymentEventData.OrderId);

            // STEP 2: Handle different event types
            // Event Grid allows subscribers to filter by event type
            // This function handles all types, but you could create separate functions
            switch (eventGridEvent.EventType)
            {
                case "PaymentCompleted":
                    await HandlePaymentCompleted(paymentEventData);
                    break;

                case "PaymentFailed":
                    await HandlePaymentFailed(paymentEventData);
                    break;

                default:
                    _logger.LogWarning("Unknown event type: {EventType}", eventGridEvent.EventType);
                    break;
            }

            _logger.LogInformation(
                "Successfully processed event {EventId}",
                eventGridEvent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing Event Grid event {EventId}",
                eventGridEvent.Id);

            // Throwing an exception causes Event Grid to retry
            // Event Grid will retry with exponential backoff
            // After max retries, event goes to dead letter destination if configured
            throw;
        }
    }

    /*
     * HANDLE PAYMENT COMPLETED
     * 
     * This is where webhooks come into play.
     * External systems (shipping, inventory, accounting) need to be notified.
     * 
     * WEBHOOK CONCEPT:
     * - HTTP callbacks to external systems
     * - "Event subscription over HTTP"
     * - External system provides a URL
     * - We POST event data to that URL
     * - They process and respond with HTTP status
     * 
     * Why use webhooks:
     * - Real-time notifications to external systems
     * - No polling required on their end
     * - Industry standard (Stripe, GitHub, Shopify all use webhooks)
     * - Loosely coupled integration
     */
    private async Task HandlePaymentCompleted(PaymentEventData eventData)
    {
        _logger.LogInformation(
            "Payment {PaymentId} completed successfully - notifying external systems",
            eventData.PaymentId);

        // Create webhook payload
        var webhookPayload = new WebhookPayload
        {
            Type = "payment.completed",
            Data = new PaymentEvent
            {
                EventType = "PaymentCompleted",
                PaymentId = eventData.PaymentId,
                OrderId = eventData.OrderId,
                CustomerId = eventData.CustomerId,
                Amount = eventData.Amount,
                Currency = eventData.Currency,
                Status = eventData.Status,
                TransactionId = eventData.TransactionId,
                OccurredAt = eventData.OccurredAt
            }
        };

        // WEBHOOK DELIVERY PATTERN:
        // 1. Send webhook to shipping system (so they can prepare order)
        // 2. Send webhook to inventory system (so they can update stock)
        // 3. Each webhook is sent independently
        // 4. Failures are logged but don't block other webhooks

        // Send to shipping system
        var shippingWebhookUrl = Environment.GetEnvironmentVariable("ShippingWebhookUrl");
        if (!string.IsNullOrEmpty(shippingWebhookUrl))
        {
            await _webhookService.SendWebhook(
                shippingWebhookUrl,
                webhookPayload,
                "payment.completed");
        }

        // Send to inventory system
        var inventoryWebhookUrl = Environment.GetEnvironmentVariable("InventoryWebhookUrl");
        if (!string.IsNullOrEmpty(inventoryWebhookUrl))
        {
            await _webhookService.SendWebhook(
                inventoryWebhookUrl,
                webhookPayload,
                "payment.completed");
        }

        // In a real system, you might also:
        // - Update order status in database
        // - Trigger email confirmation (could be another Service Bus message)
        // - Record in data warehouse for analytics
        // - Update customer's purchase history
    }

    /*
     * HANDLE PAYMENT FAILED
     * 
     * Failed payments need different handling:
     * - Notify customer
     * - Update order status
     * - Potentially retry with different payment method
     */
    private async Task HandlePaymentFailed(PaymentEventData eventData)
    {
        _logger.LogWarning(
            "Payment {PaymentId} failed - notifying relevant systems",
            eventData.PaymentId);

        var webhookPayload = new WebhookPayload
        {
            Type = "payment.failed",
            Data = new PaymentEvent
            {
                EventType = "PaymentFailed",
                PaymentId = eventData.PaymentId,
                OrderId = eventData.OrderId,
                CustomerId = eventData.CustomerId,
                Amount = eventData.Amount,
                Currency = eventData.Currency,
                Status = eventData.Status,
                OccurredAt = eventData.OccurredAt
            }
        };

        // Notify systems of payment failure
        var shippingWebhookUrl = Environment.GetEnvironmentVariable("ShippingWebhookUrl");
        if (!string.IsNullOrEmpty(shippingWebhookUrl))
        {
            await _webhookService.SendWebhook(
                shippingWebhookUrl,
                webhookPayload,
                "payment.failed");
        }
    }

    // Helper class to deserialize Event Grid data
    private class PaymentEventData
    {
        public string PaymentId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? TransactionId { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
