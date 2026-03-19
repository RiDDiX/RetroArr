using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Notifications;

namespace RetroArr.Api.V3.Webhooks
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly RetroArrDbContext _context;
        private readonly IWebhookService _webhookService;

        public WebhookController(RetroArrDbContext context, IWebhookService webhookService)
        {
            _context = context;
            _webhookService = webhookService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAll()
        {
            var webhooks = await _context.Webhooks
                .OrderBy(w => w.Name)
                .ToListAsync();

            return Ok(webhooks.Select(w => new
            {
                w.Id,
                w.Name,
                w.Url,
                w.Method,
                w.Events,
                EventsList = GetEventsList(w.Events),
                w.Enabled,
                w.LastTriggeredAt,
                w.LastResponseCode,
                w.LastError,
                w.CreatedAt,
                w.UpdatedAt
            }));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetById(int id)
        {
            var webhook = await _context.Webhooks.FindAsync(id);
            if (webhook == null)
                return NotFound();

            return Ok(new
            {
                webhook.Id,
                webhook.Name,
                webhook.Url,
                webhook.Method,
                webhook.Headers,
                webhook.PayloadTemplate,
                webhook.Events,
                EventsList = GetEventsList(webhook.Events),
                webhook.Enabled,
                webhook.LastTriggeredAt,
                webhook.LastResponseCode,
                webhook.LastError,
                webhook.CreatedAt,
                webhook.UpdatedAt
            });
        }

        [HttpPost]
        public async Task<ActionResult<Webhook>> Create([FromBody] CreateWebhookRequest request)
        {
            var webhook = new Webhook
            {
                Name = request.Name,
                Url = request.Url,
                Method = request.Method ?? "POST",
                Headers = request.Headers,
                PayloadTemplate = request.PayloadTemplate,
                Events = request.Events,
                Enabled = request.Enabled ?? true
            };

            _context.Webhooks.Add(webhook);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = webhook.Id }, webhook);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Webhook>> Update(int id, [FromBody] UpdateWebhookRequest request)
        {
            var webhook = await _context.Webhooks.FindAsync(id);
            if (webhook == null)
                return NotFound();

            if (request.Name != null) webhook.Name = request.Name;
            if (request.Url != null) webhook.Url = request.Url;
            if (request.Method != null) webhook.Method = request.Method;
            if (request.Headers != null) webhook.Headers = request.Headers;
            if (request.PayloadTemplate != null) webhook.PayloadTemplate = request.PayloadTemplate;
            if (request.Events.HasValue) webhook.Events = request.Events.Value;
            if (request.Enabled.HasValue) webhook.Enabled = request.Enabled.Value;

            webhook.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(webhook);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var webhook = await _context.Webhooks.FindAsync(id);
            if (webhook == null)
                return NotFound();

            _context.Webhooks.Remove(webhook);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/test")]
        public async Task<ActionResult> Test(int id)
        {
            var webhook = await _context.Webhooks.FindAsync(id);
            if (webhook == null)
                return NotFound();

            try
            {
                // Send a test event
                await _webhookService.TriggerAsync(WebhookEvents.OnScanCompleted, new
                {
                    test = true,
                    message = "This is a test webhook from RetroArr",
                    timestamp = DateTime.UtcNow
                });

                return Ok(new { success = true, message = "Test webhook sent" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("events")]
        public ActionResult GetAvailableEvents()
        {
            var events = new[]
            {
                new { Value = (int)WebhookEvents.OnGameAdded, Name = "OnGameAdded", Description = "When a game is added to the library" },
                new { Value = (int)WebhookEvents.OnGameRemoved, Name = "OnGameRemoved", Description = "When a game is removed from the library" },
                new { Value = (int)WebhookEvents.OnDownloadStarted, Name = "OnDownloadStarted", Description = "When a download starts" },
                new { Value = (int)WebhookEvents.OnDownloadCompleted, Name = "OnDownloadCompleted", Description = "When a download completes" },
                new { Value = (int)WebhookEvents.OnDownloadFailed, Name = "OnDownloadFailed", Description = "When a download fails" },
                new { Value = (int)WebhookEvents.OnGameInstalled, Name = "OnGameInstalled", Description = "When a game is installed" },
                new { Value = (int)WebhookEvents.OnScanCompleted, Name = "OnScanCompleted", Description = "When a library scan completes" },
                new { Value = (int)WebhookEvents.OnCollectionUpdated, Name = "OnCollectionUpdated", Description = "When a collection is updated" }
            };

            return Ok(events);
        }

        private static List<string> GetEventsList(WebhookEvents events)
        {
            var list = new List<string>();
            
            if ((events & WebhookEvents.OnGameAdded) != 0) list.Add("OnGameAdded");
            if ((events & WebhookEvents.OnGameRemoved) != 0) list.Add("OnGameRemoved");
            if ((events & WebhookEvents.OnDownloadStarted) != 0) list.Add("OnDownloadStarted");
            if ((events & WebhookEvents.OnDownloadCompleted) != 0) list.Add("OnDownloadCompleted");
            if ((events & WebhookEvents.OnDownloadFailed) != 0) list.Add("OnDownloadFailed");
            if ((events & WebhookEvents.OnGameInstalled) != 0) list.Add("OnGameInstalled");
            if ((events & WebhookEvents.OnScanCompleted) != 0) list.Add("OnScanCompleted");
            if ((events & WebhookEvents.OnCollectionUpdated) != 0) list.Add("OnCollectionUpdated");

            return list;
        }
    }

    public class CreateWebhookRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? Method { get; set; }
        public string? Headers { get; set; }
        public string? PayloadTemplate { get; set; }
        public WebhookEvents Events { get; set; }
        public bool? Enabled { get; set; }
    }

    public class UpdateWebhookRequest
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Method { get; set; }
        public string? Headers { get; set; }
        public string? PayloadTemplate { get; set; }
        public WebhookEvents? Events { get; set; }
        public bool? Enabled { get; set; }
    }
}
