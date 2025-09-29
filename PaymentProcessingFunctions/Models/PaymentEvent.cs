namespace PaymentProcessingFunctions.Models;

/*
 * PAYMENT EVENT MODEL
 * 
 * This represents an event that will be published to Azure Event Grid.
 * 
 * KEY CONCEPT - Message vs Event:
 * 
 * MESSAGE (Service Bus):
 * - Contains commands or data that needs processing
 * - "Process this payment request"
 * - Producer cares about who processes it
 * - Usually has a specific intended recipient
 * 
 * EVENT (Event Grid):
 * - Announces that something happened
 * - "Payment was completed" (past tense)
 * - Producer doesn't care who listens
 * - Anyone interested can subscribe
 * - Represents a fact about the past
 * 
 * When to use Event Grid:
 * - Notifying external systems via webhooks
 * - Broadcasting system-wide events
 * - Loosely coupled architectures
 * - When you need pub/sub with webhooks
 * - High throughput scenarios (millions of events/sec)
 */

public class PaymentEvent
{
    /// <summary>
    /// Unique identifier for this event
    /// Different from PaymentId - this identifies the event itself
    /// Used for deduplication on the subscriber side
    /// </summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Type of event that occurred
    /// Event Grid can route events based on this type
    /// Examples: "PaymentCompleted", "PaymentFailed", "PaymentRefunded"
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// The payment identifier this event is about
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;

    /// <summary>
    /// Order identifier for tracing
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Customer identifier
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Payment amount
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Currency code
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Final status of the payment
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Transaction ID from the payment gateway
    /// This is the reference number from Stripe, PayPal, etc.
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>
    /// When the event occurred (timestamp of the actual event, not when published)
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional event data that webhook subscribers might need
    /// Different webhooks might need different information
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }
}

/*
 * WEBHOOK PAYLOAD MODEL
 * 
 * This is what external systems receive when they subscribe to our webhooks.
 * This follows a common webhook pattern with signature verification.
 */
public class WebhookPayload
{
    /// <summary>
    /// Webhook event identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Type of webhook event
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// When the webhook was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The actual event data
    /// </summary>
    public PaymentEvent Data { get; set; } = new();

    /// <summary>
    /// Signature for webhook verification
    /// In production, this would be an HMAC signature that the receiver can verify
    /// This ensures the webhook actually came from your system
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// API version for backward compatibility
    /// If you change your webhook format, you can maintain multiple versions
    /// </summary>
    public string ApiVersion { get; set; } = "2025-01-01";
}
