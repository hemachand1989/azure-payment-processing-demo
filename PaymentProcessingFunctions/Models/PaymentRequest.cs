namespace PaymentProcessingFunctions.Models;

/*
 * PAYMENT REQUEST MODEL
 * 
 * This represents the initial payment request that comes from the customer.
 * This is the message that will be sent to Azure Service Bus Queue.
 * 
 * Key Concepts:
 * - This is a Data Transfer Object (DTO) that carries information between systems
 * - It will be serialized to JSON when sent to Service Bus
 * - Contains all information needed to process a payment
 */

public class PaymentRequest
{
    /// <summary>
    /// Unique identifier for this payment request
    /// Used for idempotency - ensuring we don't process the same payment twice
    /// </summary>
    public string PaymentId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Order identifier from the e-commerce system
    /// Links the payment back to the customer's order
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Customer identifier for tracking and fraud detection
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Payment amount in decimal format (e.g., 99.99)
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Currency code (USD, EUR, GBP, etc.)
    /// Following ISO 4217 standard
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Payment method details (credit card, PayPal, etc.)
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; } = new();

    /// <summary>
    /// Customer's billing address for verification
    /// </summary>
    public Address? BillingAddress { get; set; }

    /// <summary>
    /// Timestamp when the payment was initiated
    /// Useful for tracking processing time and detecting stale requests
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional metadata for additional tracking
    /// Can include campaign codes, referral sources, etc.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/*
 * PAYMENT METHOD MODEL
 * 
 * Contains the actual payment instrument details.
 * In a real system, sensitive data like card numbers would be tokenized.
 */
public class PaymentMethod
{
    /// <summary>
    /// Type of payment method (CreditCard, DebitCard, PayPal, etc.)
    /// </summary>
    public string Type { get; set; } = "CreditCard";

    /// <summary>
    /// Card number (should be tokenized in production!)
    /// This is simplified for demo purposes
    /// In production, use a payment tokenization service like Stripe, Adyen
    /// </summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>
    /// Card expiry month (1-12)
    /// </summary>
    public int ExpiryMonth { get; set; }

    /// <summary>
    /// Card expiry year (e.g., 2025)
    /// </summary>
    public int ExpiryYear { get; set; }

    /// <summary>
    /// Card verification value (CVV/CVC)
    /// Should never be stored - only used for transaction
    /// </summary>
    public string Cvv { get; set; } = string.Empty;

    /// <summary>
    /// Cardholder name as it appears on the card
    /// </summary>
    public string CardholderName { get; set; } = string.Empty;
}

/*
 * ADDRESS MODEL
 * 
 * Billing address for payment verification and fraud detection.
 */
public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
