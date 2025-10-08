using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace TestApp.EventHubApp.WebAPI.Helpers
{

    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Azure.Storage.Blobs;
    using System.IO;
    using System.Text;
    using System.Text.Json; // Added for JsonSerializer

    // --- FILE 3: Encompass API Client (Mock) ---

    /// <summary>
    /// Mock client to simulate calls to an external Encompass API and Azure Blob Storage.
    /// </summary>
    public class EncompassApiClient
    {
        private readonly ILogger<EncompassApiClient> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public EncompassApiClient(ILogger<EncompassApiClient> logger, BlobServiceClient blobServiceClient)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        /// <summary>
        /// Simulates calling Encompass to fetch comprehensive loan data.
        /// Used by FieldChangeWorker and MilestoneWorker.
        /// </summary>
        /// <param name="loanId">The ID of the loan to fetch.</param>
        /// <returns>A JSON string representing the mock loan data.</returns>
        public Task<string> GetLoanDataAsync(string loanId)
        {
            _logger.LogDebug("MOCK: Calling Encompass API to fetch full loan data for {LoanId}...", loanId);

            // Returns mocked JSON data
            var mockData = JsonSerializer.Serialize(new
            {
                LoanId = loanId,
                DataStatus = "FullDataFetched",
                Source = "Encompass",
                Timestamp = DateTime.UtcNow
            });

            return Task.FromResult(mockData);
        }

        /// <summary>
        /// Simulates calling Encompass to fetch specific loan condition data.
        /// Used by ConditionWorker.
        /// </summary>
        /// <param name="loanId">The ID of the loan.</param>
        /// <param name="conditionId">The ID of the condition.</param>
        /// <returns>A JSON string representing the mock condition data.</returns>
        public Task<string> GetLoanConditionDataAsync(string loanId, string conditionId)
        {
            _logger.LogDebug("MOCK: Calling Encompass API to fetch condition data for {ConditionId} on loan {LoanId}...", conditionId, loanId);

            // Returns mocked JSON data
            var mockData = JsonSerializer.Serialize(new
            {
                LoanId = loanId,
                ConditionId = conditionId,
                Status = "Pending",
                Details = "Condition data retrieved from mock API.",
                Timestamp = DateTime.UtcNow
            });

            return Task.FromResult(mockData);
        }

        /// <summary>
        /// Stores loan data as a JSON blob in Azurite.
        /// </summary>
        /// <param name="loanId">The loan ID.</param>
        /// <param name="data">The data string to store.</param>
        /// <param name="subfolder">The virtual subfolder (e.g., 'field-change-data') for organization.</param>
        public async Task StoreLoanDataBlobAsync(string loanId, string data, string subfolder)
        {
            // The container used for storing the actual loan data (different from checkpoint container)
            var containerClient = _blobServiceClient.GetBlobContainerClient("loan-data-store");
            await containerClient.CreateIfNotExistsAsync();

            // Ensure the blob name is unique
            var blobName = $"{subfolder}/{loanId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";
            var blobClient = containerClient.GetBlobClient(blobName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            // Overwrite if it exists (true)
            await blobClient.UploadAsync(stream, overwrite: true);

            _logger.LogInformation("MOCK: Stored data for Loan {LoanId} in Blob: {BlobName}", loanId, blobName);
        }
    }

}
