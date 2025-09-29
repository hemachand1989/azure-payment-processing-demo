using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PaymentProcessingFunctions.Models;
using System.Net;
using System.Text.Json;

namespace PaymentProcessingFunctions.Functions;

public class PaymentInitiation
{
    private readonly ILogger<PaymentInitiation> _logger;
    private readonly ServiceBusClient _serviceBusClient;

    public PaymentInitiation(
        ILogger<PaymentInitiation> logger,
        ServiceBusClient serviceBusClient)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
    }

    // CHANGED: AuthorizationLevel.Anonymous for testing (use AuthorizationLevel.Function in production)
    [Function("InitiatePayment")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "payments")]
        HttpRequestData req)
    {
        _logger.LogInformation("Payment initiation request received");

        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var paymentRequest = JsonSerializer.Deserialize<PaymentRequest>(
                requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (paymentRequest == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid payment request");
            }

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

            await SendToServiceBusQueue(paymentRequest);

            _logger.LogInformation(
                "Payment request {PaymentId} for order {OrderId} queued successfully",
                paymentRequest.PaymentId,
                paymentRequest.OrderId);

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

    private async Task SendToServiceBusQueue(PaymentRequest paymentRequest)
    {
        await using var sender = _serviceBusClient.CreateSender("payment-requests");

        var messageBody = JsonSerializer.Serialize(paymentRequest);

        var message = new ServiceBusMessage(messageBody)
        {
            MessageId = paymentRequest.PaymentId,
            ContentType = "application/json",
            Subject = "PaymentRequest",
            CorrelationId = paymentRequest.OrderId
        };

        message.ApplicationProperties.Add("OrderId", paymentRequest.OrderId);
        message.ApplicationProperties.Add("Amount", paymentRequest.Amount);
        message.ApplicationProperties.Add("Currency", paymentRequest.Currency);
        message.ApplicationProperties.Add("CustomerId", paymentRequest.CustomerId);

        await sender.SendMessageAsync(message);

        _logger.LogInformation(
            "Message sent to queue with MessageId: {MessageId}",
            message.MessageId);
    }

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
