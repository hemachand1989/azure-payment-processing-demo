using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentProcessingFunctions.Models;
using PaymentProcessingFunctions.Services;
using System.Text.Json;

namespace PaymentProcessingFunctions.Functions;

/*
 * PAYMENT VALIDATOR FUNCTION - SERVICE BUS QUEUE TRIGGER
 * 
 * This function is triggered automatically when a message arrives in the Service Bus Queue.
 * It demonstrates the CONSUMER side of the Queue pattern.
 * 
 * FLOW:
 * 1. Message appears in "payment-requests" queue
 * 2. Azure Functions runtime automatically retrieves the message
 * 3. This function is invoked with the message
 * 4. Validates payment details
 * 5. Runs fraud detection
 * 6. Publishes result to Service Bus TOPIC (not queue)
 * 7. Completes or abandons the message based on success/failure
 * 
 * KEY SERVICE BUS QUEUE CONCEPTS:
 * 
 * - ServiceBusTrigger: Automatically polls the queue for messages
 * - AutoCompleteMessages = false: We manually control message completion
 * - Why manual completion? So we can retry on failure or move to dead letter
 * - PeekLock mode (default): Message is locked but not deleted until we complete it
 * 
 * MESSAGE LIFECYCLE:
 * 1. Message arrives in queue
 * 2. Function retrieves it (message is "locked" - invisible to other consumers)
 * 3. If processing succeeds: CompleteMessageAsync() - message is deleted
 * 4. If processing fails: AbandonMessageAsync() - message goes back to queue for retry
 * 5. After max retries (5 by default): Message moves to Dead Letter Queue
 * 
 * DEAD LETTER QUEUE:
 * A special sub-queue that holds messages that couldn't be processed
 * Useful for:
 * - Debugging why messages failed
 * - Manual intervention
 * - Reprocessing after fixing the issue
 */

public class PaymentValidator
{
    private readonly ILogger<PaymentValidator> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IFraudDetectionService _fraudService;

    public PaymentValidator(
        ILogger<PaymentValidator> logger,
        ServiceBusClient serviceBusClient,
        IFraudDetectionService fraudService)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        _fraudService = fraudService;
    }

    /*
     * SERVICE BUS QUEUE TRIGGER FUNCTION
     * 
     * Attributes explained:
     * - [Function]: Marks this as an Azure Function
     * - [ServiceBusTrigger]: Triggers on queue messages
     * - queueName: "payment-requests" - the queue to listen to
     * - Connection: Name of the connection string setting in configuration
     * 
     * Parameters explained:
     * - ServiceBusReceivedMessage message: The actual message from the queue
     * - ServiceBusMessageActions messageActions: Operations to perform on the message
     *   (Complete, Abandon, DeadLetter, etc.)
     */
    [Function("PaymentValidator")]
    public async Task Run(
        [ServiceBusTrigger("payment-requests", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        _logger.LogInformation(
            "Processing payment validation for MessageId: {MessageId}, DeliveryCount: {DeliveryCount}",
            message.MessageId,
            message.DeliveryCount);

        try
        {
            // STEP 1: Deserialize the message body to our PaymentRequest object
            var messageBody = message.Body.ToString();
            var paymentRequest = JsonSerializer.Deserialize<PaymentRequest>(messageBody);

            if (paymentRequest == null)
            {
                _logger.LogError("Failed to deserialize payment request from message {MessageId}",
                    message.MessageId);
                
                // Dead letter this message - it's malformed and will never succeed
                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Message body could not be deserialized");
                return;
            }

            // STEP 2: Check for idempotency
            // In a real system, check if we've already processed this PaymentId
            // This prevents duplicate processing if the message is retried
            _logger.LogInformation(
                "Validating payment {PaymentId} for order {OrderId}",
                paymentRequest.PaymentId,
                paymentRequest.OrderId);

            // STEP 3: Perform business validation
            var validationResult = ValidatePayment(paymentRequest);

            // STEP 4: Run fraud detection
            // This could be a call to an external service or ML model
            var fraudScore = await _fraudService.CalculateFraudScore(paymentRequest);

            // STEP 5: Create payment status based on validation and fraud check
            var paymentStatus = new PaymentStatus
            {
                PaymentId = paymentRequest.PaymentId,
                OrderId = paymentRequest.OrderId,
                CustomerId = paymentRequest.CustomerId,
                Amount = paymentRequest.Amount,
                Currency = paymentRequest.Currency,
                FraudScore = fraudScore,
                FraudFlagged = fraudScore > 70, // Flag if fraud score > 70
                ValidationMessages = validationResult.Errors,
                ValidatedAt = DateTime.UtcNow
            };

            // Determine status based on validation and fraud check
            if (!validationResult.IsValid)
            {
                paymentStatus.Status = PaymentStatusType.ValidationFailed;
            }
            else if (fraudScore > 70)
            {
                paymentStatus.Status = PaymentStatusType.FraudReview;
            }
            else
            {
                paymentStatus.Status = PaymentStatusType.Validated;
            }

            // STEP 6: Publish to Service Bus TOPIC
            // This is where we switch from QUEUE to TOPIC
            // Multiple subscribers can now process this status independently
            await PublishToServiceBusTopic(paymentStatus);

            // STEP 7: Complete the message
            // This tells Service Bus we're done and the message can be deleted
            await messageActions.CompleteMessageAsync(message);

            _logger.LogInformation(
                "Payment {PaymentId} validated successfully with status {Status}",
                paymentRequest.PaymentId,
                paymentStatus.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing payment validation for MessageId: {MessageId}",
                message.MessageId);

            // Check if we've exceeded retry attempts
            if (message.DeliveryCount >= 3)
            {
                // After 3 attempts, move to dead letter queue
                _logger.LogWarning(
                    "Message {MessageId} exceeded retry limit, moving to dead letter queue",
                    message.MessageId);

                await messageActions.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "MaxDeliveryCountExceeded",
                    deadLetterErrorDescription: ex.Message);
            }
            else
            {
                // Abandon the message so it can be retried
                // The message goes back to the queue and will be picked up again
                await messageActions.AbandonMessageAsync(message);
            }
        }
    }

    /*
     * PUBLISHING TO SERVICE BUS TOPIC
     * 
     * KEY DIFFERENCE: Queue vs Topic
     * 
     * QUEUE (what we consumed from):
     * - Point-to-point messaging
     * - One message → One consumer
     * - Used for commands/tasks
     * 
     * TOPIC (what we're publishing to):
     * - Publish-subscribe messaging
     * - One message → Multiple subscribers
     * - Each subscriber gets their own copy
     * - Used for events/broadcasts
     * 
     * Why use a Topic here?
     * Multiple independent services need to react to payment status:
     * 1. Payment Processor - Charges the card if validated
     * 2. Notification Service - Sends email/SMS
     * 3. Analytics Service - Records metrics
     * 
     * Each service has its own subscription to the topic and processes independently.
     */
    private async Task PublishToServiceBusTopic(PaymentStatus status)
    {
        // Create a sender for the TOPIC (not queue)
        await using var sender = _serviceBusClient.CreateSender("payment-status");

        var messageBody = JsonSerializer.Serialize(status);
        var message = new ServiceBusMessage(messageBody)
        {
            MessageId = Guid.NewGuid().ToString(), // New ID for the topic message
            ContentType = "application/json",
            Subject = "PaymentStatus",
            CorrelationId = status.PaymentId // Links back to original payment
        };

        // Application properties for filtering
        // Subscribers can filter messages based on these properties
        // Example: NotificationService only subscribes to Completed/Failed statuses
        message.ApplicationProperties.Add("PaymentId", status.PaymentId);
        message.ApplicationProperties.Add("Status", status.Status.ToString());
        message.ApplicationProperties.Add("FraudFlagged", status.FraudFlagged);
        message.ApplicationProperties.Add("Amount", status.Amount);

        await sender.SendMessageAsync(message);

        _logger.LogInformation(
            "Payment status published to topic for PaymentId: {PaymentId} with Status: {Status}",
            status.PaymentId,
            status.Status);
    }

    /*
     * VALIDATION LOGIC
     * 
     * In a real system, this would be more comprehensive:
     * - Check card number validity (Luhn algorithm)
     * - Verify CVV format
     * - Check expiry date
     * - Validate billing address
     * - Check against blocked cards list
     */
    private ValidationResult ValidatePayment(PaymentRequest request)
    {
        var result = new ValidationResult();

        // Check card expiry
        var expiryDate = new DateTime(
            request.PaymentMethod.ExpiryYear,
            request.PaymentMethod.ExpiryMonth,
            1).AddMonths(1).AddDays(-1);

        if (expiryDate < DateTime.UtcNow)
        {
            result.Errors.Add("Card has expired");
        }

        // Validate card number format (simplified)
        if (request.PaymentMethod.CardNumber.Length < 13 ||
            request.PaymentMethod.CardNumber.Length > 19)
        {
            result.Errors.Add("Invalid card number format");
        }

        // Check CVV
        if (string.IsNullOrWhiteSpace(request.PaymentMethod.Cvv) ||
            request.PaymentMethod.Cvv.Length < 3 ||
            request.PaymentMethod.Cvv.Length > 4)
        {
            result.Errors.Add("Invalid CVV");
        }

        // Validate amount
        if (request.Amount <= 0 || request.Amount > 10000)
        {
            result.Errors.Add("Amount must be between 0 and 10,000");
        }

        return result;
    }

    // Helper class for validation results
    private class ValidationResult
    {
        public List<string> Errors { get; set; } = new();
        public bool IsValid => !Errors.Any();
    }
}
