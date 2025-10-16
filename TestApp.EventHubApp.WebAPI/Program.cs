using Azure.Core.Pipeline;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using TestApp.EventHubApp.WebAPI.Helpers;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuration Setup
builder.Services.Configure<EventHubSettings>(builder.Configuration.GetSection("EventHubs"));
builder.Services.Configure<AzureStorageSettings>(builder.Configuration.GetSection("AzureStorage"));
builder.Services.Configure<ProcessingControlSettings>(builder.Configuration.GetSection("ProcessingControl"));
builder.Services.Configure<DeadLetterMonitoringSettings>(builder.Configuration.GetSection("DeadLetterMonitoring"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// 2. Client Registrations
builder.Services.AddSingleton<EventHubProducerClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<EventHubSettings>>().Value;
    // Use the emulator connection string: "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=DefaultKey"
    return new EventHubProducerClient(settings.EmulatorConnectionString, settings.EventsHubName);
});

// Mock External Services
builder.Services.AddSingleton<IDataStore, MockMongoDataStore>();
builder.Services.AddSingleton<EncompassApiClient>();

// Email and DLQ Services
builder.Services.AddSingleton<IEmailService, MockEmailService>();
builder.Services.AddSingleton<IDeadLetterQueueService, MockDeadLetterQueueService>();
builder.Services.AddSingleton<IProcessingPauseProvider, MemoryCachePauseProvider>();
builder.Services.AddSingleton<IDeadLetterProducer>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<EventHubSettings>>();
    var logger = sp.GetRequiredService<ILogger<DeadLetterProducer>>();
    return new DeadLetterProducer(settings, logger);
});
builder.Services.AddSingleton<BlobServiceClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AzureStorageSettings>>().Value;
    // Use Azurite connection string
    var blobClientOptions = new BlobClientOptions
    {
        // CRITICAL FIX 1: Explicitly set the protocol to HTTP.
        //Protocol = BlobClientProtocol.Http,

        // CRITICAL FIX 2: Define a custom transport to ensure the client connects over HTTP correctly.
        //Transport = new HttpClientTransport(
        //    new HttpClient(
        //        new SocketsHttpHandler { AllowAutoRedirect = false, 
        //        // Added this line to bypass potential SSL/TLS negotiation when running inside Docker context
        //        SslOptions = new System.Net.Security.SslClientAuthenticationOptions()
        //        } // Robust setting for local testing
        //    )
        //)
    };
    return new BlobServiceClient(settings.AzuriteConnectionString, blobClientOptions);
});

builder.Services.AddMemoryCache();

// 3. Worker Services (IHostedService)
builder.Services.AddHostedService<EventsWorker>();
builder.Services.AddHostedService<FieldChangeWorker>();
builder.Services.AddHostedService<MilestoneWorker>();
builder.Services.AddHostedService<ConditionWorker>();
builder.Services.AddHostedService<DeadLetterMonitorJob>();

builder.Services.AddControllers();
builder.Services.AddLogging(configure => configure.AddConsole());

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
