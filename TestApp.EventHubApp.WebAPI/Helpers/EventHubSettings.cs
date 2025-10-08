namespace TestApp.EventHubApp.WebAPI.Helpers
{
    /// <summary>
    /// Configuration for Event Hubs connections.
    /// </summary>
    public class EventHubSettings
    {
        // The default connection string for the Event Hubs Emulator
        public string EmulatorConnectionString { get; set; } = "Endpoint=sb://localhost:5672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=DefaultKey";

        // Names of the four event hubs
        public string EventsHubName { get; set; } = "EventHub";
        public string FieldChangeEventHubName { get; set; } = "FieldChangeEventHub";
        public string MilestoneEventHubName { get; set; } = "MilestoneEventHub";
        public string ConditionEventHubName { get; set; } = "ConditionEventHub";

        // Names of the consumer groups (must match Config.json)
        public string EventsConsumerGroup { get; set; } = "EventProcessor";
        public string FieldChangeConsumerGroup { get; set; } = "FieldChangeEventProcessor";
        public string MilestoneConsumerGroup { get; set; } = "MilestoneEventProcessor";
        public string ConditionConsumerGroup { get; set; } = "ConditionEventProcessor";
    }

    /// <summary>
    /// Configuration for Azurite Blob Storage connection (for Event Processor checkpoints).
    /// </summary>
    public class AzureStorageSettings
    {
        // The default connection string for Azurite Blob Storage
        public string AzuriteConnectionString { get; set; } = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xWFwLgZGYKGDN+L6/htSmdEQe4T/CNiUss5E2sv5pLLeB9pZlH6vmX0vEnS/v0y1QZ0dA0F3Qd9Q==;BlobEndpoint=http://azurite:10000/devstoreaccount1;";
        public string CheckpointContainerName { get; set; } = "eventhub-checkpoints";
    }
}
