using System.Text;
using System.Text.Json;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using TestApp.EventHubApp.WebAPI.Helpers;
using Microsoft.Extensions.Caching.Memory;

using EventData = Azure.Messaging.EventHubs.EventData;

namespace TestApp.EventHubApp.WebAPI.Controllers
{

    // --- API Controller ---
    [ApiController]
    [Route("[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly EventHubProducerClient _producerClient;
        private readonly ILogger<WebhookController> _logger;
        private readonly IMemoryCache _cache;

        public WebhookController(EventHubProducerClient producerClient, ILogger<WebhookController> logger, IMemoryCache cache)
        {
            _producerClient = producerClient;
            _logger = logger;
            _cache = cache;
        }

        [HttpPost("event")]
        public async Task<IActionResult> PostEvent([FromBody] RawEventModel eventData)
        {
            _logger.LogInformation("Received webhook event for loan {LoanId} of type {EventType}", eventData.LoanId, eventData.EventType);

            try
            {
                var eventBody = JsonSerializer.Serialize(eventData);
                var eventDataBatch = await _producerClient.CreateBatchAsync();
                eventDataBatch.TryAdd(new EventData(Encoding.UTF8.GetBytes(eventBody)));

                await _producerClient.SendAsync(eventDataBatch);

                _logger.LogInformation("Successfully produced event to Event Hub: {HubName}", _producerClient.EventHubName);
                return Ok(new { status = "Event Queued" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send event to Event Hub.");
                return StatusCode(500, new { status = "Internal Server Error" });
            }
        }

        [HttpPost("pause")]
        public IActionResult Pause()
        {
            MemoryCachePauseProvider.SetPaused(_cache, true);
            return Ok(new { status = "paused" });
        }

        [HttpPost("resume")]
        public IActionResult Resume()
        {
            MemoryCachePauseProvider.SetPaused(_cache, false);
            return Ok(new { status = "resumed" });
        }
    }
}
