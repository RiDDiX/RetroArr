using System;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using RetroArr.Core.Configuration;
using RetroArr.Core.MetadataSource.Steam;
using System.Diagnostics.CodeAnalysis;

using System.Threading;

namespace RetroArr.Api.V3.Steam
{
    [ApiController]
    [Route("api/v3/steam")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class SteamController : ControllerBase
    {
        private readonly ConfigurationService _configService;
        // removed _steamClient field

        public SteamController(ConfigurationService configService)
        {
            _configService = configService;
            // Instantiated per request with the latest key
        }

        [HttpGet("profile")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GetUserProfile()
        {
            try
            {
                var settings = _configService.LoadSteamSettings();
                if (!settings.IsConfigured)
                {
                    return BadRequest(new { success = false, message = "Steam not configured" });
                }

                var client = new SteamClient(settings.ApiKey);
                var profileTask = client.GetPlayerProfileAsync(settings.SteamId);
                var levelTask = client.GetSteamLevelAsync(settings.SteamId);

                await Task.WhenAll(profileTask, levelTask);

                var profile = profileTask.Result;
                var level = levelTask.Result;

                if (profile == null)
                {
                    return NotFound(new { success = false, message = "Steam profile not found or private." });
                }

                // Map to ViewModel (clean API contract)
                var viewModel = new SteamProfileViewModel
                {
                    SteamId = profile.SteamId,
                    PersonaName = profile.PersonaName,
                    AvatarUrl = profile.AvatarUrl,
                    PersonaState = profile.PersonaState,
                    GameExtraInfo = "",
                    RealName = profile.RealName ?? string.Empty,
                    CountryCode = profile.LocCountryCode ?? string.Empty,
                    AccountCreated = profile.TimeCreated > 0 ? DateTimeOffset.FromUnixTimeSeconds(profile.TimeCreated).DateTime : null,
                    Level = level
                };

                return Ok(viewModel);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("stats")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GetLibraryStats()
        {
            try
            {
                var settings = _configService.LoadSteamSettings();
                if (!settings.IsConfigured)
                {
                    return BadRequest(new { success = false, message = "Steam not configured" });
                }

                // Aggregate stats from owned games
                var client = new SteamClient(settings.ApiKey);
                var games = await client.GetOwnedGamesAsync(settings.SteamId);
                
                var stats = new SteamStatsViewModel
                {
                    TotalGames = games.Count,
                    TotalMinutesPlayed = games.Sum(g => g.PlaytimeForever),
                    TotalHoursPlayed = Math.Round(games.Sum(g => g.PlaytimeForever) / 60.0, 1)
                };

                return Ok(stats);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("recent")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GetRecentGames()
        {
            try
            {
                var settings = _configService.LoadSteamSettings();
                if (!settings.IsConfigured)
                {
                    return BadRequest(new { success = false, message = "Steam not configured" });
                }

                var client = new SteamClient(settings.ApiKey);
                var recentGames = await client.GetRecentlyPlayedGamesAsync(settings.SteamId);
                
                // Limit concurrency to avoid Steam 429 Rate Limits
                using var semaphore = new SemaphoreSlim(3);

                var fetchTasks = recentGames.Select(async g => 
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // Fetch News and Achievements in parallel for each game
                        var newsTask = client.GetGameNewsAsync(g.AppId.ToString(), 3, 300);
                        var achievementsTask = client.GetPlayerAchievementsAsync(settings.SteamId, g.AppId.ToString());

                        await Task.WhenAll(newsTask, achievementsTask);

                        var newsItems = newsTask.Result;
                        var (achieved, total) = achievementsTask.Result;
                        var latestNews = newsItems.FirstOrDefault();

                        return new SteamGameViewModel
                        {
                            AppId = g.AppId,
                            Name = g.Name,
                            Playtime2Weeks = g.Playtime2Weeks,
                            PlaytimeForever = g.PlaytimeForever,
                            // Use Steam Header Image (460x215) instead of the tiny icon
                            IconUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{g.AppId}/header.jpg",
                            Achieved = achieved,
                            TotalAchievements = total,
                            CompletionPercent = total > 0 ? Math.Round((double)achieved / total * 100, 1) : 0,
                            LatestNews = latestNews != null ? new SteamNewsItemViewModel 
                            {
                                Title = latestNews.Title,
                                Url = latestNews.Url,
                                FeedLabel = latestNews.FeedLabel,
                                Date = latestNews.Date
                            } : null
                        };
                    }
                    finally
                    {
                         semaphore.Release();
                    }
                });

                var mappedGames = (await Task.WhenAll(fetchTasks)).ToList();

                return Ok(mappedGames);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("friends")]
        [ResponseCache(Duration = 300)] // cache for 5 minutes
        public async Task<IActionResult> GetFriends()
        {
            try
            {
                var settings = _configService.LoadSteamSettings();
                if (!settings.IsConfigured)
                {
                    return BadRequest(new { success = false, message = "Steam not configured" });
                }

                var client = new SteamClient(settings.ApiKey);
                var friendIds = await client.GetFriendListAsync(settings.SteamId);
                
                if (!friendIds.Any()) return Ok(new List<SteamFriendViewModel>());

                var profiles = await client.GetPlayerProfilesAsync(friendIds);

                var friends = profiles.Select(p => new SteamFriendViewModel
                {
                    SteamId = p.SteamId,
                    PersonaName = p.PersonaName,
                    AvatarUrl = p.AvatarUrl,
                    PersonaState = p.PersonaState,
                    GameExtraInfo = p.GameExtraInfo ?? ""
                })
                // Sort: In-Game (Playing...) first, then Online (1), then others
                .OrderByDescending(f => !string.IsNullOrEmpty(f.GameExtraInfo)) // Playing first
                .ThenByDescending(f => f.PersonaState == 1) // Online second
                .ThenBy(f => f.PersonaName) // Then alphabetical
                .ToList();

                return Ok(friends);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
