using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;

namespace RetroArr.Core.Notifications
{
    public class WebhookService : IWebhookService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly RetroArrDbContext _context;
        private readonly HttpClient _httpClient;

        public WebhookService(RetroArrDbContext context, HttpClient? httpClient = null)
        {
            _context = context;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task TriggerAsync(WebhookEvents eventType, object? data)
        {
            var webhooks = await _context.Webhooks
                .Where(w => w.Enabled && (w.Events & eventType) == eventType)
                .ToListAsync();

            foreach (var webhook in webhooks)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendWebhookAsync(webhook, eventType, data);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Webhook] Error triggering {webhook.Name}: {ex.Message}");
                    }
                });
            }
        }

        private async Task SendWebhookAsync(Webhook webhook, WebhookEvents eventType, object? data)
        {
            try
            {
                var payload = new WebhookPayload
                {
                    Event = eventType.ToString(),
                    Timestamp = DateTime.UtcNow,
                    Data = data
                };

                string jsonPayload;
                
                if (!string.IsNullOrEmpty(webhook.PayloadTemplate))
                {
                    jsonPayload = ProcessTemplate(webhook.PayloadTemplate, payload);
                }
                else
                {
                    jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    });
                }

                var request = new HttpRequestMessage(
                    new HttpMethod(webhook.Method),
                    webhook.Url
                );

                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Add custom headers
                if (!string.IsNullOrEmpty(webhook.Headers))
                {
                    try
                    {
                        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(webhook.Headers);
                        if (headers != null)
                        {
                            foreach (var header in headers)
                            {
                                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            }
                        }
                    }
                    catch { }
                }

                var response = await _httpClient.SendAsync(request);
                
                webhook.LastTriggeredAt = DateTime.UtcNow;
                webhook.LastResponseCode = (int)response.StatusCode;
                webhook.LastError = response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync();
                webhook.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.Info($"[Webhook] {webhook.Name} triggered for {eventType}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                webhook.LastTriggeredAt = DateTime.UtcNow;
                webhook.LastError = ex.Message;
                webhook.UpdatedAt = DateTime.UtcNow;
                
                try { await _context.SaveChangesAsync(); } catch { }
                
                throw;
            }
        }

        private string ProcessTemplate(string template, WebhookPayload payload)
        {
            var result = template;
            
            // Replace common variables
            result = result.Replace("{event}", payload.Event);
            result = result.Replace("{timestamp}", payload.Timestamp.ToString("o"));
            
            // For complex data replacement, serialize the data
            if (payload.Data != null)
            {
                var dataJson = JsonSerializer.Serialize(payload.Data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                result = result.Replace("{data}", dataJson);
            }

            return result;
        }
    }
}
