using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.EventGrid;
using Azure;
using PaymentProcessingFunctions.Services;

/*
 * PROGRAM.CS - Function App Entry Point
 * 
 * This file configures the Azure Functions host and sets up dependency injection.
 * It's the starting point for the entire application.
 * 
 * Key Components:
 * - HostBuilder: Configures the Function App runtime
 * - ServiceBus Client: Manages connections to Azure Service Bus
 * - EventGrid Publisher: Sends events to Event Grid
 * - Dependency Injection: Registers services for use in functions
 */

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // Add Application Insights for monitoring and telemetry
        services.AddApplicationInsightsTelemetryWorkerService();

        // Register ServiceBusClient as a singleton
        // Singleton pattern ensures we reuse the same connection across all function invocations
        // This is more efficient than creating a new connection each time
        services.AddSingleton(sp =>
        {
            var connectionString = Environment.GetEnvironmentVariable("ServiceBusConnection")
                ?? throw new InvalidOperationException("ServiceBusConnection not configured");
            
            return new ServiceBusClient(connectionString);
        });

        // Register EventGridPublisherClient as a singleton
        // This client publishes events to Azure Event Grid
        services.AddSingleton(sp =>
        {
            var endpoint = Environment.GetEnvironmentVariable("EventGridTopicEndpoint")
                ?? throw new InvalidOperationException("EventGridTopicEndpoint not configured");
            var key = Environment.GetEnvironmentVariable("EventGridTopicKey")
                ?? throw new InvalidOperationException("EventGridTopicKey not configured");
            
            return new EventGridPublisherClient(
                new Uri(endpoint),
                new AzureKeyCredential(key)
            );
        });

        // Register our custom services
        services.AddSingleton<IPaymentService, PaymentService>();
        services.AddSingleton<IFraudDetectionService, FraudDetectionService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IWebhookService, WebhookService>();
    })
    .Build();

host.Run();
