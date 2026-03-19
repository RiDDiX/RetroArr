using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RetroArr.Core.Notifications
{
    /// <summary>
    /// Webhook configuration for sending notifications to external services
    /// </summary>
    public class Webhook
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string Url { get; set; } = string.Empty;
        
        /// <summary>
        /// HTTP method (POST, PUT, etc.)
        /// </summary>
        [MaxLength(10)]
        public string Method { get; set; } = "POST";
        
        /// <summary>
        /// Custom headers as JSON (e.g., Authorization)
        /// </summary>
        public string? Headers { get; set; }
        
        /// <summary>
        /// Custom payload template (supports variables like {game.title})
        /// </summary>
        public string? PayloadTemplate { get; set; }
        
        /// <summary>
        /// Events that trigger this webhook
        /// </summary>
        public WebhookEvents Events { get; set; } = WebhookEvents.None;
        
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Last time this webhook was triggered
        /// </summary>
        public DateTime? LastTriggeredAt { get; set; }
        
        /// <summary>
        /// Last response status code
        /// </summary>
        public int? LastResponseCode { get; set; }
        
        /// <summary>
        /// Last error message if failed
        /// </summary>
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
    
    /// <summary>
    /// Payload sent to webhooks
    /// </summary>
    public class WebhookPayload
    {
        public string Event { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public object? Data { get; set; }
    }
    
    /// <summary>
    /// Service for triggering webhooks
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    public interface IWebhookService
    {
        Task TriggerAsync(WebhookEvents eventType, object? data);
    }
}
