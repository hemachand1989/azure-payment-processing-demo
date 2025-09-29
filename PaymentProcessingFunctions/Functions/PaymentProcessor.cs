using Azure.Messaging.EventGrid;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentProcessingFunctions.Models;
using PaymentProcessingFunctions.Services;
using System.Text.Json;

namespace PaymentProcessingFunctions.Functions;

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
            var messageBody = message.Body.ToString();
            var paymentStatus = JsonSerializer.Deserialize<PaymentStatus>(messageBody);

            if (paymentStatus == null)
            {
                await messageActions.DeadLetterMessageAsync(
                    message,
                    new Dictionary<string, object>
                    {
                        { "DeadLetterReason", "DeserializationFailed" },
                        { "DeadLetterErrorDescription", "Could not deserialize PaymentStatus" }
                    });
                return;
            }

            _logger.LogInformation(
                "Processing payment {PaymentId} with status {Status}",
                paymentStatus.PaymentId,
                paymentStatus.Status);

            if (paymentStatus.Status != PaymentStatusType.Validated)
            {
                _logger.LogInformation(
                    "Skipping payment {PaymentId} - Status is {Status}, not Validated",
                    paymentStatus.PaymentId,
                    paymentStatus.Status);

                await messageActions.CompleteMessageAsync(message);
                return;
            }

            if (paymentStatus.FraudFlagged)
            {
                _logger.LogWarning(
                    "Payment {PaymentId} flagged for fraud review (score: {Score})",
                    paymentStatus.PaymentId,
                    paymentStatus.FraudScore);

                await messageActions.CompleteMessageAsync(message);
                return;
            }

            _logger.LogInformation(
                "Charging payment gateway for {Amount} {Currency}",
                paymentStatus.Amount,
                paymentStatus.Currency);

            var processingResult = await _paymentService.ProcessPayment(
                paymentStatus.PaymentId,
                paymentStatus.Amount,
                paymentStatus.Currency);

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

            if (message.DeliveryCount >= 3)
            {
                await messageActions.DeadLetterMessageAsync(
                    message,
                    new Dictionary<string, object>
                    {
                        { "DeadLetterReason", "ProcessingFailed" },
                        { "DeadLetterErrorDescription", ex.Message }
                    });
            }
            else
            {
                await messageActions.AbandonMessageAsync(message);
            }
        }
    }

    private async Task PublishToEventGrid(PaymentEvent paymentEvent)
    {
        // Create the event data object
        var eventData = new
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
        };

        // Serialize to JSON and create BinaryData
        var jsonData = JsonSerializer.Serialize(eventData);
        var binaryData = BinaryData.FromString(jsonData);

        // Create Event Grid event
        var eventGridEvent = new EventGridEvent(
            subject: $"payments/{paymentEvent.PaymentId}",
            eventType: paymentEvent.EventType,
            dataVersion: "1.0",
            data: binaryData)
        {
            Id = paymentEvent.EventId,
            EventTime = paymentEvent.OccurredAt
        };

        await _eventGridPublisher.SendEventAsync(eventGridEvent);

        _logger.LogInformation(
            "Event published to Event Grid: {EventType} for PaymentId: {PaymentId}",
            paymentEvent.EventType,
            paymentEvent.PaymentId);
    }
}
