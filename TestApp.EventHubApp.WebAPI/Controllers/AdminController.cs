using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using TestApp.EventHubApp.WebAPI.Helpers;

namespace TestApp.EventHubApp.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly IMemoryCache _cache;
        private readonly IDeadLetterQueueService _dlqService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IMemoryCache cache, 
            IDeadLetterQueueService dlqService,
            ILogger<AdminController> logger)
        {
            _cache = cache;
            _dlqService = dlqService;
            _logger = logger;
        }

        [HttpPost("pause")]
        public IActionResult PauseProcessing()
        {
            MemoryCachePauseProvider.SetPaused(_cache, true);
            _logger.LogInformation("Processing paused via Admin API");
            return Ok(new { status = "paused", timestamp = DateTime.UtcNow });
        }

        [HttpPost("resume")]
        public IActionResult ResumeProcessing()
        {
            MemoryCachePauseProvider.SetPaused(_cache, false);
            _logger.LogInformation("Processing resumed via Admin API");
            return Ok(new { status = "resumed", timestamp = DateTime.UtcNow });
        }

        [HttpGet("status")]
        public IActionResult GetProcessingStatus()
        {
            var pauseProvider = new MemoryCachePauseProvider(_cache, null);
            var isPaused = pauseProvider.IsPaused();
            return Ok(new { 
                isPaused = isPaused,
                status = isPaused ? "paused" : "running",
                timestamp = DateTime.UtcNow 
            });
        }

        [HttpGet("dlq-count")]
        public async Task<IActionResult> GetDeadLetterQueueCount()
        {
            try
            {
                var count = await _dlqService.GetApproximateMessageCountAsync();
                return Ok(new { 
                    count = count,
                    timestamp = DateTime.UtcNow 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting DLQ count");
                return StatusCode(500, new { error = "Failed to get DLQ count" });
            }
        }

        [HttpPost("dlq-test-count/{count}")]
        public IActionResult SetTestDlqCount(int count)
        {
            // Only available for mock service testing
            if (_dlqService is MockDeadLetterQueueService)
            {
                MockDeadLetterQueueService.SetMockCount(count);
                _logger.LogInformation("Mock DLQ count set to {Count} for testing", count);
                return Ok(new { mockCount = count, timestamp = DateTime.UtcNow });
            }
            
            return BadRequest(new { error = "Test count only available with mock service" });
        }

        [HttpPost("reprocess-dlq")]
        public IActionResult TriggerDeadLetterReprocessing()
        {
            // Placeholder for DLQ reprocessing trigger
            _logger.LogInformation("Dead letter reprocessing triggered via Admin API");
            return Ok(new { 
                status = "reprocessing_triggered", 
                message = "Dead letter events will be reprocessed",
                timestamp = DateTime.UtcNow 
            });
        }
    }
}
