using System.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Options;
using EventData = Azure.Messaging.EventHubs.EventData;

namespace TestApp.EventHubApp.WebAPI.Helpers
{

    // --- Event Processing Base Class ---
    public abstract class EventProcessorBase<TWorker> : BackgroundService
    {
        protected readonly ILogger<TWorker> _logger;
        protected readonly EventHubSettings _settings;
        protected readonly AzureStorageSettings _storageSettings;
        protected readonly BlobServiceClient _blobServiceClient;
        protected readonly string _hubName;
        protected readonly string _consumerGroup;
        protected EventProcessorClient _processorClient;

        public EventProcessorBase(
            IOptions<EventHubSettings> settings,
            IOptions<AzureStorageSettings> storageSettings,
            BlobServiceClient blobServiceClient,
            ILogger<TWorker> logger,
            string hubName,
            string consumerGroup)
        {
            _logger = logger;
            _settings = settings.Value;
            _storageSettings = storageSettings.Value;
            _blobServiceClient = blobServiceClient;
            _hubName = hubName;
            _consumerGroup = consumerGroup;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{WorkerName} starting up, connecting to hub '{HubName}'...", typeof(TWorker).Name, _hubName);

            try
            {
                // 1. Create Checkpoint Store (BlobContainer)
                var containerClient = _blobServiceClient.GetBlobContainerClient(_storageSettings.CheckpointContainerName);
                await containerClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

                // 2. Create Event Processor Client
                _processorClient = new EventProcessorClient(
                    containerClient,
                    _consumerGroup,
                    _settings.EmulatorConnectionString,
                    _hubName);

                _processorClient.ProcessEventAsync += HandleEvent;
                _processorClient.ProcessErrorAsync += HandleError;

                await _processorClient.StartProcessingAsync(stoppingToken);

                // Wait until the service is stopped
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected when the application shuts down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{WorkerName} failed during startup or operation.", typeof(TWorker).Name);
            }
            finally
            {
                if (_processorClient != null)
                {
                    await _processorClient.StopProcessingAsync();
                }
                _logger.LogInformation("{WorkerName} stopped.", typeof(TWorker).Name);
            }
        }

        protected abstract Task HandleEvent(ProcessEventArgs eventArgs);

        protected Task HandleError(ProcessErrorEventArgs eventArgs)
        {
            _logger.LogError(eventArgs.Exception,
                "Error in Event Processor for hub '{HubName}' (Partition: {PartitionId}, Action: {Action})",
                _hubName, eventArgs.PartitionId, eventArgs.Operation);
            return Task.CompletedTask;
        }
    }

    // --- Specialized Event Processing Workers ---

    /// <summary>
    /// Worker 1: Reads from Events Hub, stores in MongoDB, and routes to other hubs.
    /// </summary>
    public class EventsWorker : EventProcessorBase<EventsWorker>
    {
        private readonly IDataStore _dataStore;
        private readonly EventHubProducerClient _fieldChangeProducer;
        private readonly EventHubProducerClient _milestoneProducer;
        private readonly EventHubProducerClient _conditionProducer;

        public EventsWorker(
            IOptions<EventHubSettings> settings,
            IOptions<AzureStorageSettings> storageSettings,
            BlobServiceClient blobServiceClient,
            IDataStore dataStore,
            ILogger<EventsWorker> logger)
            : base(settings, storageSettings, blobServiceClient, logger, settings.Value.EventsHubName, settings.Value.EventsConsumerGroup)
        {
            _dataStore = dataStore;

            // Initialize producers for the downstream hubs
            _fieldChangeProducer = new EventHubProducerClient(_settings.EmulatorConnectionString, _settings.FieldChangeEventHubName);
            _milestoneProducer = new EventHubProducerClient(_settings.EmulatorConnectionString, _settings.MilestoneEventHubName);
            _conditionProducer = new EventHubProducerClient(_settings.EmulatorConnectionString, _settings.ConditionEventHubName);
        }

        protected override async Task HandleEvent(ProcessEventArgs eventArgs)
        {
            try
            {
                var eventBody = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
                var rawEvent = JsonSerializer.Deserialize<RawEventModel>(eventBody);

                _logger.LogInformation("EventsWorker: Processing LoanId: {LoanId}, EventType: {EventType}", rawEvent.LoanId, rawEvent.EventType);

                // 1. Store in MongoDB (Mocked)
                await _dataStore.SaveEventAsync(rawEvent);
                _logger.LogInformation("EventsWorker: Saved event to MongoDB for LoanId: {LoanId}", rawEvent.LoanId);

                // 2. Route Event to next hub based on type
                EventHubProducerClient targetProducer = null;
                string targetHubName = string.Empty;

                switch (rawEvent.EventType.ToLowerInvariant())
                {
                    case "fieldchange":
                        targetProducer = _fieldChangeProducer;
                        targetHubName = _settings.FieldChangeEventHubName;
                        break;
                    case "milestone":
                        targetProducer = _milestoneProducer;
                        targetHubName = _settings.MilestoneEventHubName;
                        break;
                    case "condition":
                        targetProducer = _conditionProducer;
                        break;
                    default:
                        _logger.LogWarning("EventsWorker: Unknown event type '{EventType}'. Skipping forwarding.", rawEvent.EventType);
                        break;
                }

                if (targetProducer != null)
                {
                    var eventDataBatch = await targetProducer.CreateBatchAsync();
                    eventDataBatch.TryAdd(new EventData(Encoding.UTF8.GetBytes(eventBody)));
                    await targetProducer.SendAsync(eventDataBatch);
                    _logger.LogInformation("EventsWorker: Forwarded event {EventType} to {HubName}", rawEvent.EventType, targetHubName);
                }

                // 3. Update Checkpoint
                await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EventsWorker: Error processing or routing event.");
            }
        }
    }


    /// <summary>
    /// Worker 2: Reads from FieldChangeEventHub, checks specific field, calls Encompass API, and stores to Blob.
    /// </summary>
    public class FieldChangeWorker : EventProcessorBase<FieldChangeWorker>
    {
        private readonly EncompassApiClient _encompassClient;

        public FieldChangeWorker(
            IOptions<EventHubSettings> settings,
            IOptions<AzureStorageSettings> storageSettings,
            BlobServiceClient blobServiceClient,
            EncompassApiClient encompassClient,
            ILogger<FieldChangeWorker> logger)
            : base(settings, storageSettings, blobServiceClient, logger, settings.Value.FieldChangeEventHubName, settings.Value.FieldChangeConsumerGroup)
        {
            _encompassClient = encompassClient;
        }

        protected override async Task HandleEvent(ProcessEventArgs eventArgs)
        {
            try
            {
                var eventBody = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
                var rawEvent = JsonSerializer.Deserialize<RawEventModel>(eventBody);

                _logger.LogInformation("FieldChangeWorker: Processing LoanId: {LoanId}", rawEvent.LoanId);

                // 1. Check for TPO.X92 (Mocked field check logic)
                if (rawEvent.FieldId == "TPO.X92")
                {
                    // 2. Call Encompass API
                    var loanData = await _encompassClient.GetLoanDataAsync(rawEvent.LoanId);

                    // 3. Store loan data in Azure Blob (Mocked storage)
                    await _encompassClient.StoreLoanDataBlobAsync(rawEvent.LoanId, loanData, "field-change-data");

                    _logger.LogInformation("FieldChangeWorker: Loan {LoanId} processed for TPO.X92 and data stored.", rawEvent.LoanId);
                }

                // 4. Update Checkpoint
                await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FieldChangeWorker: Error processing event.");
            }
        }
    }

    /// <summary>
    /// Worker 3: Reads from MilestoneEventHub, calls Encompass API, and stores to Blob.
    /// </summary>
    public class MilestoneWorker : EventProcessorBase<MilestoneWorker>
    {
        private readonly EncompassApiClient _encompassClient;

        public MilestoneWorker(
            IOptions<EventHubSettings> settings,
            IOptions<AzureStorageSettings> storageSettings,
            BlobServiceClient blobServiceClient,
            EncompassApiClient encompassClient,
            ILogger<MilestoneWorker> logger)
            : base(settings, storageSettings, blobServiceClient, logger, settings.Value.MilestoneEventHubName, settings.Value.MilestoneConsumerGroup)
        {
            _encompassClient = encompassClient;
        }

        protected override async Task HandleEvent(ProcessEventArgs eventArgs)
        {
            try
            {
                var eventBody = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
                var rawEvent = JsonSerializer.Deserialize<RawEventModel>(eventBody);

                _logger.LogInformation("MilestoneWorker: Processing LoanId: {LoanId}, Milestone: {Milestone}", rawEvent.LoanId, rawEvent.MilestoneName);

                // 1. Call Encompass API
                var loanData = await _encompassClient.GetLoanDataAsync(rawEvent.LoanId);

                // 2. Store loan data in Azure Blob (Mocked storage)
                await _encompassClient.StoreLoanDataBlobAsync(rawEvent.LoanId, loanData, "milestone-data");

                _logger.LogInformation("MilestoneWorker: Loan {LoanId} milestone data stored.", rawEvent.LoanId);

                // 3. Update Checkpoint
                await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MilestoneWorker: Error processing event.");
            }
        }
    }

    /// <summary>
    /// Worker 4: Reads from ConditionEventHub, calls Encompass API, and stores condition data to Blob.
    /// </summary>
    public class ConditionWorker : EventProcessorBase<ConditionWorker>
    {
        private readonly EncompassApiClient _encompassClient;

        public ConditionWorker(
            IOptions<EventHubSettings> settings,
            IOptions<AzureStorageSettings> storageSettings,
            BlobServiceClient blobServiceClient,
            EncompassApiClient encompassClient,
            ILogger<ConditionWorker> logger)
            : base(settings, storageSettings, blobServiceClient, logger, settings.Value.ConditionEventHubName, settings.Value.ConditionConsumerGroup)
        {
            _encompassClient = encompassClient;
        }

        protected override async Task HandleEvent(ProcessEventArgs eventArgs)
        {
            try
            {
                var eventBody = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
                var rawEvent = JsonSerializer.Deserialize<RawEventModel>(eventBody);

                _logger.LogInformation("ConditionWorker: Processing LoanId: {LoanId}, Condition: {Condition}", rawEvent.LoanId, rawEvent.ConditionId);

                // 1. Call Encompass API for condition data (Mocked)
                var conditionData = await _encompassClient.GetLoanConditionDataAsync(rawEvent.LoanId, rawEvent.ConditionId);

                // 2. Store condition data in Azure Blob (Mocked storage)
                await _encompassClient.StoreLoanDataBlobAsync(rawEvent.LoanId, conditionData, "condition-data");

                _logger.LogInformation("ConditionWorker: Loan {LoanId} condition data stored.", rawEvent.LoanId);

                // 3. Update Checkpoint
                await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConditionWorker: Error processing event.");
            }
        }
    }

    // --- Data Models (for Webhook and Internal Processing) ---
    public record RawEventModel(
        string LoanId,
        string EventType,
        string FieldId, // Used by FieldChangeWorker
        string MilestoneName, // Used by MilestoneWorker
        string ConditionId // Used by ConditionWorker
    );

    /// <summary>
    /// Abstraction of the Event Hub Producer Client to allow for mocking in unit tests.
    /// </summary>
    public interface IEventHubProducer
    {
        /// <summary>
        /// Gets the name of the Event Hub this producer is configured for.
        /// </summary>
        string EventHubName { get; }

        /// <summary>
        /// Creates a new batch of events and attempts to add the provided event data.
        /// </summary>
        /// <param name="eventData">The event data to send.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        Task SendEventAsync(Azure.Messaging.EventHubs.EventData eventData);
    }

    // --- Real Implementation Wrapper ---

    /// <summary>
    /// Implements IEventHubProducer using the real Azure EventHubProducerClient.
    /// </summary>
    public class RealEventHubProducer : IEventHubProducer
    {
        private readonly EventHubProducerClient _client;
        private readonly ILogger<RealEventHubProducer> _logger;

        /// <inheritdoc/>
        public string EventHubName => _client.EventHubName;

        public RealEventHubProducer(
            EventHubProducerClient client,
            ILogger<RealEventHubProducer> logger)
        {
            _client = client;
            _logger = logger;
        }

        /// <summary>
        /// Creates a batch and sends a single event to the Event Hub.
        /// </summary>
        /// <param name="eventData">The event data to send.</param>
        public async Task SendEventAsync(EventData eventData)
        {
            try
            {
                using EventDataBatch eventDataBatch = await _client.CreateBatchAsync();
                if (!eventDataBatch.TryAdd(eventData))
                {
                    _logger.LogError("Event was too large to be added to the batch.");
                    throw new InvalidOperationException("Event batch full or event too large.");
                }

                await _client.SendAsync(eventDataBatch);
                _logger.LogInformation("Successfully sent 1 event to Event Hub: {HubName}", EventHubName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send event to Event Hub '{HubName}'.", EventHubName);
                throw;
            }
        }

    }


    /// <summary>
    /// A mock producer for unit testing purposes. It only logs the operation 
    /// instead of connecting to a real Event Hub.
    /// </summary>
    public class MockEventHubProducer : IEventHubProducer
    {
        private readonly ILogger<MockEventHubProducer> _logger;

        /// <inheritdoc/>
        public string EventHubName { get; }

        public MockEventHubProducer(ILogger<MockEventHubProducer> logger, string eventHubName)
        {
            _logger = logger;
            EventHubName = eventHubName;
        }

        /// <summary>
        /// Simulates the send operation by logging the event content.
        /// </summary>
        public Task SendEventAsync(EventData eventData)
        {
            var body = System.Text.Encoding.UTF8.GetString(eventData.EventBody.ToArray());
            _logger.LogInformation("MOCK: Simulated sending event to {HubName}. Content: {Body}", EventHubName, body);
            return Task.CompletedTask;
        }
    }


}
