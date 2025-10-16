using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace TestApp.EventHubApp.WebAPI.Helpers
{
    public interface IDeadLetterQueueService
    {
        Task<int> GetApproximateMessageCountAsync();
    }

    public class DeadLetterQueueService : IDeadLetterQueueService
    {
        private readonly EventHubSettings _eventHubSettings;
        private readonly AzureStorageSettings _storageSettings;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<DeadLetterQueueService> _logger;

        public DeadLetterQueueService(
            IOptions<EventHubSettings> eventHubSettings,
            IOptions<AzureStorageSettings> storageSettings,
            BlobServiceClient blobServiceClient,
            ILogger<DeadLetterQueueService> logger)
        {
            _eventHubSettings = eventHubSettings.Value;
            _storageSettings = storageSettings.Value;
            _blobServiceClient = blobServiceClient;
            _logger = logger;
        }

        public async Task<int> GetApproximateMessageCountAsync()
        {
            try
            {
                // Create a temporary processor client to get partition information
                var containerClient = _blobServiceClient.GetBlobContainerClient(_storageSettings.CheckpointContainerName);
                await containerClient.CreateIfNotExistsAsync();

                // Use EventHubConsumerClient to retrieve partition IDs instead of EventProcessorClient
                var consumerClient = new EventHubConsumerClient(
                    _eventHubSettings.DeadLetterConsumerGroup,
                    _eventHubSettings.EmulatorConnectionString,
                    _eventHubSettings.DeadLetterEventHubName);

                var partitionIds = await consumerClient.GetPartitionIdsAsync();

                int totalCount = 0;

                foreach (var partitionId in partitionIds)
                {
                    try
                    {
                        var partitionProperties = await consumerClient.GetPartitionPropertiesAsync(partitionId);

                        // Get the last checkpoint for this partition
                        var checkpointBlobName = $"{_eventHubSettings.DeadLetterEventHubName}/{_eventHubSettings.DeadLetterConsumerGroup}/{partitionId}";
                        var checkpointBlob = containerClient.GetBlobClient(checkpointBlobName);

                        long lastProcessedSequenceNumber = -1;
                        if (await checkpointBlob.ExistsAsync())
                        {
                            // In a real implementation, you'd parse the checkpoint blob to get the last processed sequence number
                            // For now, we'll use a simplified approach
                        }

                        // Calculate approximate count as difference between last sequence number and last processed
                        var approximateCount = Math.Max(0, partitionProperties.LastEnqueuedSequenceNumber - lastProcessedSequenceNumber);
                        totalCount += (int)approximateCount;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error getting count for partition {PartitionId}", partitionId);
                    }
                }

                return totalCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dead letter queue count");
                return 0;
            }
        }
    }

    public class MockDeadLetterQueueService : IDeadLetterQueueService
    {
        private static int _mockCount = 0;
        private readonly ILogger<MockDeadLetterQueueService> _logger;

        public MockDeadLetterQueueService(ILogger<MockDeadLetterQueueService> logger)
        {
            _logger = logger;
        }

        public Task<int> GetApproximateMessageCountAsync()
        {
            // Simulate increasing DLQ count for testing
            _mockCount += Random.Shared.Next(0, 5);
            _logger.LogDebug("MOCK: Dead letter queue count: {Count}", _mockCount);
            return Task.FromResult(_mockCount);
        }

        public static void ResetMockCount() => _mockCount = 0;
        public static void SetMockCount(int count) => _mockCount = count;
    }
}
