using System.Text;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp.EventHubApp.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EventsController> _logger;
        // NOTE: The connection string used for the Event Hubs Emulator:
        // Endpoint=sb://localhost:5672/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=anything

        // The Event Hubs Emulator only needs the connection string without EntityPath for runtime info.
        private const string EventHubsEmulatorConnStringKey = "EventHubs:EmulatorConnectionString";
        private readonly string _eventHubsEmulatorConnString;

        public EventsController(IConfiguration configuration, ILogger<EventsController> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventHubsEmulatorConnString = _configuration.GetValue<string>(EventHubsEmulatorConnStringKey)
                                        ?? throw new InvalidOperationException($"Configuration key '{EventHubsEmulatorConnStringKey}' is missing or empty.");
        }

        /// <summary>
        /// Retrieves the status (partition count, last sequence number, and offset) for all partitions
        /// of a given Event Hub. This gives an idea of the event count.
        /// </summary>
        [HttpGet("status/{hubName}")]
        public async Task<IActionResult> GetHubStatus(string hubName)
        {
            _logger.LogInformation("Attempting to get status for Event Hub: {HubName}", hubName);

            // Use a temporary EventHubConsumerClient (using $Default consumer group) to get runtime information.
            // The connection string must point to the root namespace (without EntityPath).
            await using var consumerClient = new EventHubConsumerClient(
                EventHubConsumerClient.DefaultConsumerGroupName,
                $"{_eventHubsEmulatorConnString}", // Assuming this is the root namespace connection string
                hubName);

            try
            {
                var partitionIds = await consumerClient.GetPartitionIdsAsync();
                var statusList = new List<object>();

                foreach (var partitionId in partitionIds)
                {
                    var partitionProperties = await consumerClient.GetPartitionPropertiesAsync(partitionId);

                    // Note: Calculating "count" accurately requires checking the starting offset.
                    // For simplicity, we just return the properties, including LastEnqueuedSequenceNumber.
                    statusList.Add(new
                    {
                        PartitionId = partitionProperties.Id,
                        LastEnqueuedTime = partitionProperties.LastEnqueuedTime,
                        LastEnqueuedSequenceNumber = partitionProperties.LastEnqueuedSequenceNumber,
                        IsEmpty = partitionProperties.IsEmpty,
                        IncomingEventCount = partitionProperties.LastEnqueuedSequenceNumber // Simplified "count" approximation
                    });
                }

                return Ok(new { HubName = hubName, Status = statusList });
            }
            catch (EventHubsException ex)
            {
                _logger.LogError(ex, "Event Hubs exception while getting status for {HubName}.", hubName);
                return StatusCode(500, new { error = "Event Hubs Error", message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while getting status for {HubName}.", hubName);
                return StatusCode(500, new { error = "Internal Server Error", message = ex.Message });
            }
        }

        /// <summary>
        /// Peeks (reads without consuming) the latest event from each partition of the Event Hub.
        /// </summary>
        [HttpGet("peek/{hubName}")]
        public async Task<IActionResult> PeekLatestEvents(string hubName)
        {
            _logger.LogInformation("Attempting to peek latest events for Event Hub: {HubName}", hubName);

            // Use a temporary EventHubConsumerClient to read a small batch of events.
            await using var consumerClient = new EventHubConsumerClient(
                EventHubConsumerClient.DefaultConsumerGroupName,
                $"{_eventHubsEmulatorConnString}",
                hubName);

            try
            {
                var partitionIds = await consumerClient.GetPartitionIdsAsync();
                var peekedEvents = new List<object>();

                // Set options to read from the end of the stream (latest events)
                var readOptions = new ReadEventOptions { MaximumWaitTime = TimeSpan.FromSeconds(2) };

                foreach (var partitionId in partitionIds)
                {
                    // Read events from the latest available position
                    await foreach (var partitionEvent in consumerClient.ReadEventsFromPartitionAsync(
                        partitionId,
                        EventPosition.Latest,
                        readOptions,
                        CancellationToken.None))
                    {
                        var eventData = partitionEvent.Data;
                        if (eventData != null)
                        {
                            var body = Encoding.UTF8.GetString(eventData.EventBody.ToArray());
                            peekedEvents.Add(new
                            {
                                PartitionId = partitionEvent.Partition.PartitionId,
                                SequenceNumber = eventData.SequenceNumber,
                                EnqueuedTime = eventData.EnqueuedTime,
                                Body = body
                            });
                        }

                        // Break after reading the first event from the partition
                        break;
                    }
                }

                return Ok(new { HubName = hubName, Events = peekedEvents.OrderByDescending(e => (DateTime)((dynamic)e).EnqueuedTime) });
            }
            catch (EventHubsException ex)
            {
                _logger.LogError(ex, "Event Hubs exception while peeking events for {HubName}.", hubName);
                return StatusCode(500, new { error = "Event Hubs Error", message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while peeking events for {HubName}.", hubName);
                return StatusCode(500, new { error = "Internal Server Error", message = ex.Message });
            }
        }
    }
}

