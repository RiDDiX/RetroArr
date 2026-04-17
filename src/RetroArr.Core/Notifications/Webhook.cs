using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RetroArr.Core.Notifications
{
    public class Webhook
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Url { get; set; } = string.Empty;

        [MaxLength(10)]
        public string Method { get; set; } = "POST";

        // JSON map, e.g. for Authorization
        public string? Headers { get; set; }

        // supports variables like {game.title}
        public string? PayloadTemplate { get; set; }

        public WebhookEvents Events { get; set; } = WebhookEvents.None;

        public bool Enabled { get; set; } = true;

        public DateTime? LastTriggeredAt { get; set; }
        public int? LastResponseCode { get; set; }
        public string? LastError { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    [Flags]
    public enum WebhookEvents
    {
        None = 0,
        OnGameAdded = 1,
        OnGameRemoved = 2,
        OnDownloadStarted = 4,
        OnDownloadCompleted = 8,
        OnDownloadFailed = 16,
        OnGameInstalled = 32,
        OnScanCompleted = 64,
        OnCollectionUpdated = 128,
        All = OnGameAdded | OnGameRemoved | OnDownloadStarted | OnDownloadCompleted |
              OnDownloadFailed | OnGameInstalled | OnScanCompleted | OnCollectionUpdated
    }

    public class WebhookPayload
    {
        public string Event { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public object? Data { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    public interface IWebhookService
    {
        Task TriggerAsync(WebhookEvents eventType, object? data);
    }
}
