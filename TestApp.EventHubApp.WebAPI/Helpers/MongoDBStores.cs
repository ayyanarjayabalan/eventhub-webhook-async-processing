namespace TestApp.EventHubApp.WebAPI.Helpers
{
    // --- FILE 2: IDataStore.cs (Interface and Mock Implementation) ---

    /// <summary>
    /// Interface for a MongoDB data store service.
    /// </summary>
    public interface IDataStore
    {
        Task SaveEventAsync(RawEventModel eventData);
        // ... other MongoDB operations
    }

    /// <summary>
    /// Mock implementation of IDataStore to simulate MongoDB interaction.
    /// </summary>
    public class MockMongoDataStore : IDataStore
    {
        private readonly ILogger<MockMongoDataStore> _logger;

        public MockMongoDataStore(ILogger<MockMongoDataStore> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Simulates saving an event to a MongoDB collection.
        /// </summary>
        public Task SaveEventAsync(RawEventModel eventData)
        {
            // In a real application, this would use the MongoDB C# driver.
            _logger.LogDebug("MOCK: Saving event {EventId} for Loan {LoanId} to MongoDB.", Guid.NewGuid(), eventData.LoanId);
            return Task.CompletedTask;
        }
    }
}
