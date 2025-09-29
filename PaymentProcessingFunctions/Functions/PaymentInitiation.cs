using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PaymentProcessingFunctions.Models;
using System.Net;
using System.Text.Json;

namespace PaymentProcessingFunctions.Functions;

/*
 * PAYMENT INITIATION FUNCTION - HTTP TRIGGER
 * 
 * This is the entry point for payment processing.
 * It's an HTTP-triggered Azure Function that acts as an API Gateway.
 * 
 * FLOW:
 * 1. Customer submits payment through your website/app
 * 2. This function receives the HTTP POST request
 * 3. Performs basic validation
 * 4. Sends message to Azure Service Bus QUEUE
 * 5. Returns immediate acknowledgment to customer
 * 
 * WHY USE A QUEUE?
 * - Decoupling: API responds immediately, actual processing happens asynchronously
 * - Reliability: If processing fails, message stays in queue for retry
 * - Load Leveling: Queue absorbs spikes in traffic
 * - FIFO: Payments processed in order they're received
 * 
 * AZURE FUNCTIONS CONCEPTS:
 * - [Function]: Marks a method as an Azure Function
 * - FunctionName: Name shown in Azure Portal
 * - HttpTrigger: Function triggered by HTTP request
 * - AuthorizationLevel.Function: Requires function key to call (security)
 */

public class PaymentInitiation
{
    private readonly ILogger<PaymentInitiation> _logger;
    private readonly ServiceBusClient _serviceBusClient;

    // Constructor Dependency Injection
    // These dependencies are provided by Program.cs configuration
    public PaymentInitiation(
        ILogger<PaymentInitiation> logger,
        ServiceBusClient serviceBusClient)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
    }

    [Function("InitiatePayment")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "payments")]
        HttpRequestData req)
    {
        _logger.LogInformation("Payment initiation request received");

        try
        {
            // STEP 1: Read and deserialize the incoming request body
            // This converts JSON to our PaymentRequest object
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var paymentRequest = JsonSerializer.Deserialize<PaymentRequest>(
                requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (paymentRequest == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid payment request");
            }

            // STEP 2: Basic request validation
            // In production, use FluentValidation or DataAnnotations for thorough validation
            var validationErrors = ValidatePaymentRequest(paymentRequest);
            if (validationErrors.Any())
            {
                _logger.LogWarning("Payment request validation failed: {Errors}",
                    string.Join(", ", validationErrors));
                return await CreateErrorResponse(
                    req,
                    HttpStatusCode.BadRequest,
                    "Validation failed",
                    validationErrors);
            }

            // STEP 3: Send message to Service Bus Queue
            // This is the QUEUE pattern - only one consumer will process this message
            // The queue name "payment-requests" should exist in your Service Bus namespace
            await SendToServiceBusQueue(paymentRequest);

            _logger.LogInformation(
                "Payment request {PaymentId} for order {OrderId} queued successfully",
                paymentRequest.PaymentId,
                paymentRequest.OrderId);

            // STEP 4: Return immediate success response to the customer
            // The actual payment processing happens asynchronously
            // This gives the customer fast feedback without waiting for the full process
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                paymentId = paymentRequest.PaymentId,
                status = "received",
                message = "Payment request received and is being processed",
                timestamp = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment initiation");
            return await CreateErrorResponse(
                req,
                HttpStatusCode.InternalServerError,
                "An error occurred processing your payment request");
        }
    }

    /*
     * SENDING MESSAGE TO SERVICE BUS QUEUE
     * 
     * Key Concepts:
     * - ServiceBusSender: Sends messages to a specific queue or topic
     * - ServiceBusMessage: Wraps your data with metadata
     * - MessageId: Used for deduplication
     * - ContentType: Helps receivers understand the message format
     * - ApplicationProperties: Custom metadata (like headers in HTTP)
     * 
     * Best Practices:
     * - Always use MessageId for idempotency
     * - Set ContentType for proper deserialization
     * - Use ApplicationProperties for routing/filtering metadata
     * - Handle message creation in a using block for proper disposal
     */
    private async Task SendToServiceBusQueue(PaymentRequest paymentRequest)
    {
        // Create a sender for the specific queue
        // "payment-requests" is the queue name in Azure Service Bus
        await using var sender = _serviceBusClient.CreateSender("payment-requests");

        // Serialize the payment request to JSON
        var messageBody = JsonSerializer.Serialize(paymentRequest);

        // Create the Service Bus message
        var message = new ServiceBusMessage(messageBody)
        {
            // MessageId is critical for deduplication
            // If the same MessageId is sent twice, Service Bus can detect it
            MessageId = paymentRequest.PaymentId,

            // ContentType helps the receiver know how to deserialize
            ContentType = "application/json",

            // Subject can be used for message filtering/routing
            Subject = "PaymentRequest",

            // CorrelationId for tracking related messages
            CorrelationId = paymentRequest.OrderId
        };

        // Add custom properties that can be used for filtering without deserializing the body
        // These are like HTTP headers - metadata about the message
        message.ApplicationProperties.Add("OrderId", paymentRequest.OrderId);
        message.ApplicationProperties.Add("Amount", paymentRequest.Amount);
        message.ApplicationProperties.Add("Currency", paymentRequest.Currency);
        message.ApplicationProperties.Add("CustomerId", paymentRequest.CustomerId);

        // Send the message to the queue
        // This is an async operation that completes when Service Bus acknowledges receipt
        await sender.SendMessageAsync(message);

        _logger.LogInformation(
            "Message sent to queue with MessageId: {MessageId}",
            message.MessageId);
    }

    /*
     * VALIDATION LOGIC
     * 
     * Performs basic business rule validation before queueing the request.
     * This prevents invalid requests from entering the processing pipeline.
     */
    private List<string> ValidatePaymentRequest(PaymentRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.OrderId))
            errors.Add("OrderId is required");

        if (string.IsNullOrWhiteSpace(request.CustomerId))
            errors.Add("CustomerId is required");

        if (request.Amount <= 0)
            errors.Add("Amount must be greater than zero");

        if (string.IsNullOrWhiteSpace(request.Currency))
            errors.Add("Currency is required");

        // Validate payment method
        if (request.PaymentMethod == null)
        {
            errors.Add("Payment method is required");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.PaymentMethod.CardNumber))
                errors.Add("Card number is required");

            if (request.PaymentMethod.ExpiryMonth < 1 || request.PaymentMethod.ExpiryMonth > 12)
                errors.Add("Invalid expiry month");

            if (request.PaymentMethod.ExpiryYear < DateTime.UtcNow.Year)
                errors.Add("Card has expired");

            if (string.IsNullOrWhiteSpace(request.PaymentMethod.Cvv) ||
                request.PaymentMethod.Cvv.Length < 3)
                errors.Add("Invalid CVV");
        }

        return errors;
    }

    /*
     * ERROR RESPONSE HELPER
     * 
     * Creates standardized error responses for the API
     */
    private async Task<HttpResponseData> CreateErrorResponse(
        HttpRequestData req,
        HttpStatusCode statusCode,
        string message,
        List<string>? errors = null)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new
        {
            success = false,
            message,
            errors = errors ?? new List<string>(),
            timestamp = DateTime.UtcNow
        });
        return response;
    }
}
