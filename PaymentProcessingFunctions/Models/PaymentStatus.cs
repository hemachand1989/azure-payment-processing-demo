namespace PaymentProcessingFunctions.Models;

/*
 * PAYMENT STATUS MODEL
 * 
 * This represents the status of a payment after validation/processing.
 * This message is published to Azure Service Bus TOPIC (not queue).
 * 
 * Key Difference from PaymentRequest:
 * - PaymentRequest goes to a QUEUE (one consumer)
 * - PaymentStatus goes to a TOPIC (multiple subscribers)
 * 
 * Why use a Topic?
 * Multiple independent systems need to know about payment status:
 * - Payment processor needs to charge the card
 * - Notification service needs to send confirmation email
 * - Analytics service needs to record metrics
 * - Fraud detection might need to update risk scores
 */

public class PaymentStatus
{
    /// <summary>
    /// Unique payment identifier (same as from PaymentRequest)
    /// This links the status back to the original request
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;

    /// <summary>
    /// Order identifier
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Customer identifier
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the payment
    /// </summary>
    public PaymentStatusType Status { get; set; }

    /// <summary>
    /// Amount that was validated/processed
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Currency code
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Fraud risk score (0-100, where 100 is highest risk)
    /// Calculated by fraud detection service
    /// </summary>
    public int FraudScore { get; set; }

    /// <summary>
    /// Was this payment flagged for fraud review?
    /// </summary>
    public bool FraudFlagged { get; set; }

    /// <summary>
    /// Validation messages or errors
    /// </summary>
    public List<string> ValidationMessages { get; set; } = new();

    /// <summary>
    /// When the validation occurred
    /// </summary>
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional context that subscribers might need
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/*
 * PAYMENT STATUS TYPE ENUM
 * 
 * Represents the various states a payment can be in during its lifecycle.
 */
public enum PaymentStatusType
{
    /// <summary>
    /// Payment validation passed, ready for processing
    /// </summary>
    Validated,

    /// <summary>
    /// Payment validation failed (invalid card, expired, etc.)
    /// </summary>
    ValidationFailed,

    /// <summary>
    /// Flagged by fraud detection, needs manual review
    /// </summary>
    FraudReview,

    /// <summary>
    /// Currently being processed by payment gateway
    /// </summary>
    Processing,

    /// <summary>
    /// Payment successfully completed
    /// </summary>
    Completed,

    /// <summary>
    /// Payment processing failed (insufficient funds, declined, etc.)
    /// </summary>
    Failed,

    /// <summary>
    /// Payment was refunded
    /// </summary>
    Refunded,

    /// <summary>
    /// Payment is on hold (e.g., for verification)
    /// </summary>
    OnHold
}
