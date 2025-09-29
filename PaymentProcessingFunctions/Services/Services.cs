using PaymentProcessingFunctions.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace PaymentProcessingFunctions.Services;

/*
 * WEBHOOK SERVICE
 * 
 * This service handles sending HTTP webhooks to external systems.
 * 
 * WEBHOOK BEST PRACTICES DEMONSTRATED:
 * 
 * 1. SIGNATURE VERIFICATION:
 *    - Generate HMAC signature of the payload
 *    - External system can verify the webhook came from you
 *    - Prevents spoofing and tampering
 * 
 * 2. RETRY LOGIC:
 *    - Webhooks can fail (network issues, system down)
 *    - Implement exponential backoff
 *    - Give up after reasonable attempts
 * 
 * 3. TIMEOUT:
 *    - Don't wait forever for response
 *    - Set reasonable timeout (e.g., 10 seconds)
 * 
 * 4. IDEMPOTENCY:
 *    - Include unique webhook ID
 *    - External system can deduplicate if webhook is retried
 * 
 * 5. VERSIONING:
 *    - Include API version in payload
 *    - Allows you to evolve webhook format without breaking clients
 * 
 * WEBHOOK DELIVERY PATTERNS:
 * 
 * FIRE AND FORGET:
 * - Send webhook, don't wait for response
 * - Fast but no guarantee of receipt
 * - Use for non-critical notifications
 * 
 * GUARANTEED DELIVERY:
 * - Persist webhook delivery status
 * - Retry failed deliveries
 * - Use for critical business events
 * - This implementation uses this pattern
 */

public interface IWebhookService
{
    Task<bool> SendWebhook(string url, WebhookPayload payload, string eventType);
}

public class WebhookService : IWebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly HttpClient _httpClient;
    private const int MaxRetries = 3;
    private const string WebhookSecret = "your-webhook-secret-key"; // In production: Use Azure Key Vault

    public WebhookService(ILogger<WebhookService> logger)
    {
        _logger = logger;
        
        // Configure HttpClient for webhook delivery
        _httpClient = new HttpClient
        {
            // Timeout: Don't wait forever for external systems
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /*
     * SEND WEBHOOK WITH RETRY LOGIC
     * 
     * This method:
     * 1. Generates signature for security
     * 2. Attempts delivery with retries
     * 3. Implements exponential backoff
     * 4. Logs all attempts for debugging
     */
    public async Task<bool> SendWebhook(string url, WebhookPayload payload, string eventType)
    {
        _logger.LogInformation(
            "Sending webhook to {Url} for event type {EventType}",
            url,
            eventType);

        // Generate signature for payload verification
        payload.Signature = GenerateSignature(payload);

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // RETRY LOOP with exponential backoff
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Webhook delivery attempt {Attempt}/{MaxRetries} to {Url}",
                    attempt,
                    MaxRetries,
                    url);

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Webhook delivered successfully to {Url} - Status: {StatusCode}",
                        url,
                        response.StatusCode);
                    return true;
                }

                // Log non-success status codes
                _logger.LogWarning(
                    "Webhook delivery failed to {Url} - Status: {StatusCode}, Body: {Body}",
                    url,
                    response.StatusCode,
                    await response.Content.ReadAsStringAsync());

                // Don't retry on client errors (4xx) - these won't succeed
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    _logger.LogError(
                        "Webhook delivery failed with client error {StatusCode} - not retrying",
                        response.StatusCode);
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning(
                    "Webhook delivery to {Url} timed out on attempt {Attempt}",
                    url,
                    attempt);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Webhook delivery to {Url} failed on attempt {Attempt}",
                    url,
                    attempt);
            }

            // EXPONENTIAL BACKOFF
            // Wait longer between each retry: 2s, 4s, 8s
            if (attempt < MaxRetries)
            {
                var delaySeconds = Math.Pow(2, attempt);
                _logger.LogInformation(
                    "Waiting {Delay} seconds before retry",
                    delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
        }

        _logger.LogError(
            "Webhook delivery to {Url} failed after {MaxRetries} attempts",
            url,
            MaxRetries);

        // In production:
        // - Store failed webhooks in database
        // - Create a background job to retry later
        // - Alert operations team
        // - Provide webhook delivery dashboard for customers

        return false;
    }

    /*
     * GENERATE HMAC SIGNATURE
     * 
     * Security best practice for webhooks:
     * 1. Create HMAC hash of the payload using a secret key
     * 2. Include signature in webhook headers or payload
     * 3. Receiver computes same hash with their copy of secret
     * 4. If signatures match, webhook is authentic
     * 
     * This prevents:
     * - Spoofed webhooks from attackers
     * - Tampering with webhook data in transit
     * 
     * How external systems verify:
     * 1. Extract signature from webhook
     * 2. Compute HMAC of payload with shared secret
     * 3. Compare computed signature with received signature
     * 4. Reject if they don't match
     */
    private string GenerateSignature(WebhookPayload payload)
    {
        // Serialize payload to JSON
        var json = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var secretBytes = Encoding.UTF8.GetBytes(WebhookSecret);

        // Compute HMAC-SHA256
        using var hmac = new HMACSHA256(secretBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);

        // Convert to hex string
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}

/*
 * PAYMENT SERVICE
 * 
 * Simulates integration with external payment gateway (Stripe, PayPal, etc.)
 * In production, this would make API calls to actual payment processors.
 */

public interface IPaymentService
{
    Task<PaymentResult> ProcessPayment(string paymentId, decimal amount, string currency);
}

public class PaymentService : IPaymentService
{
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(ILogger<PaymentService> logger)
    {
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPayment(string paymentId, decimal amount, string currency)
    {
        _logger.LogInformation(
            "Processing payment {PaymentId} for {Amount} {Currency}",
            paymentId,
            amount,
            currency);

        // SIMULATE PAYMENT GATEWAY CALL
        // In production, this would be:
        // - Stripe: await stripeClient.Charges.CreateAsync(...)
        // - PayPal: await paypalClient.CreatePayment(...)
        // - Adyen: await adyenClient.Authorise(...)

        try
        {
            // Simulate network delay
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Simulate 95% success rate
            var success = Random.Shared.Next(100) < 95;

            var result = new PaymentResult
            {
                Success = success,
                TransactionId = $"TXN-{Guid.NewGuid():N}",
                ResponseCode = success ? "approved" : "declined",
                Message = success ? "Payment processed successfully" : "Insufficient funds",
                ProcessedAt = DateTime.UtcNow
            };

            _logger.LogInformation(
                "Payment {PaymentId} processing result: {Success}, Transaction: {TransactionId}",
                paymentId,
                result.Success,
                result.TransactionId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment {PaymentId}", paymentId);
            throw;
        }
    }
}

public class PaymentResult
{
    public bool Success { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string ResponseCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}

/*
 * FRAUD DETECTION SERVICE
 * 
 * Simulates ML-based fraud detection.
 * In production, this might call Azure Machine Learning, custom ML models,
 * or third-party services like Sift, Riskified, or Signifyd.
 */

public interface IFraudDetectionService
{
    Task<int> CalculateFraudScore(PaymentRequest request);
}

public class FraudDetectionService : IFraudDetectionService
{
    private readonly ILogger<FraudDetectionService> _logger;

    public FraudDetectionService(ILogger<FraudDetectionService> logger)
    {
        _logger = logger;
    }

    public async Task<int> CalculateFraudScore(PaymentRequest request)
    {
        _logger.LogInformation(
            "Calculating fraud score for payment {PaymentId}",
            request.PaymentId);

        // SIMULATE FRAUD DETECTION
        // Real fraud detection considers:
        // - Transaction velocity (how many purchases recently)
        // - Device fingerprinting
        // - IP geolocation
        // - Billing address verification
        // - Historical customer behavior
        // - Card BIN analysis
        // - Machine learning model predictions

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Calculate a simplified fraud score (0-100)
        int score = 0;

        // High amount increases risk
        if (request.Amount > 1000) score += 20;
        if (request.Amount > 5000) score += 30;

        // Random component for demo
        score += Random.Shared.Next(0, 30);

        _logger.LogInformation(
            "Fraud score for payment {PaymentId}: {Score}",
            request.PaymentId,
            score);

        return Math.Min(score, 100);
    }
}

/*
 * NOTIFICATION SERVICE
 * 
 * Handles customer notifications (email, SMS, push).
 * This would subscribe to the Service Bus Topic independently.
 */

public interface INotificationService
{
    Task SendPaymentConfirmation(string customerId, PaymentStatus status);
}

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public async Task SendPaymentConfirmation(string customerId, PaymentStatus status)
    {
        _logger.LogInformation(
            "Sending payment confirmation to customer {CustomerId} for payment {PaymentId}",
            customerId,
            status.PaymentId);

        // In production:
        // - SendGrid for email
        // - Twilio for SMS
        // - Firebase for push notifications
        // - Customer.io / Braze for omnichannel messaging

        await Task.Delay(100); // Simulate sending

        _logger.LogInformation(
            "Payment confirmation sent to customer {CustomerId}",
            customerId);
    }
}
