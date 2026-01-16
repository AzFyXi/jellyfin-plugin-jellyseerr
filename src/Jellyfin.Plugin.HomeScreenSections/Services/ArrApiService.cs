using System.Text.Json;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using MediaBrowser.Model.Dto;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public enum ArrServiceType
    {
        Sonarr,
        Radarr,
        Lidarr,
        Readarr
    }

    public class ArrApiService
    {
        private readonly ILogger<ArrApiService> m_logger;
        private readonly HttpClient m_httpClient;
        private readonly ImageCacheService m_imageCacheService;

        public ArrApiService(ILogger<ArrApiService> logger, HttpClient httpClient, ImageCacheService imageCacheService)
        {
            m_logger = logger;
            m_httpClient = httpClient;
            m_imageCacheService = imageCacheService;
        }
        
        private static PluginConfiguration Config => HomeScreenSectionsPlugin.Instance?.Configuration ?? new PluginConfiguration();

        public async Task<List<BaseItemDto>> GetJellyseerrContentAsync(string endpoint, string username)
        {
            var results = new List<BaseItemDto>();
            
            string? jellyseerrUrl = Config.JellyseerrUrl;
            string? jellyseerrApiKey = Config.JellyseerrApiKey;
            string? jellyseerrExternalUrl = Config.JellyseerrExternalUrl;
            
            if (string.IsNullOrEmpty(jellyseerrUrl) || string.IsNullOrEmpty(jellyseerrApiKey))
            {
                m_logger.LogWarning("Jellyseerr URL or API Key is not configured.");
                return results;
            }

            string jellyseerrDisplayUrl = !string.IsNullOrEmpty(jellyseerrExternalUrl) ? jellyseerrExternalUrl : jellyseerrUrl;

            try
            {
                // 1. Get User ID
                using var userRequest = new HttpRequestMessage(HttpMethod.Get, $"{jellyseerrUrl.TrimEnd('/')}/api/v1/user?q={username}");
                userRequest.Headers.Add("X-Api-Key", jellyseerrApiKey);
                
                var userResponse = await m_httpClient.SendAsync(userRequest);
                if (!userResponse.IsSuccessStatusCode)
                {
                    m_logger.LogError("Failed to get Jellyseerr user. Status: {Status}", userResponse.StatusCode);
                    return results;
                }

                var userJsonStr = await userResponse.Content.ReadAsStringAsync();
                var userJson = JObject.Parse(userJsonStr);
                int? jellyseerrUserId = userJson.Value<JArray>("results")?
                    .OfType<JObject>()
                    .FirstOrDefault(x => x.Value<string>("jellyfinUsername") == username)?
                    .Value<int>("id");

                if (jellyseerrUserId == null)
                {
                    m_logger.LogWarning("Jellyseerr user not found for Jellyfin user: {Username}", username);
                    return results;
                }

                // 2. Get Content
                int page = 1;
                while (results.Count < 20 && page <= 5) // Limit pages to avoid infinite loops
                {
                    using var contentRequest = new HttpRequestMessage(HttpMethod.Get, $"{jellyseerrUrl.TrimEnd('/')}{endpoint}?page={page}");
                    contentRequest.Headers.Add("X-Api-Key", jellyseerrApiKey);
                    contentRequest.Headers.Add("X-Api-User", jellyseerrUserId.ToString());

                    var contentResponse = await m_httpClient.SendAsync(contentRequest);
                    if (!contentResponse.IsSuccessStatusCode)
                    {
                        m_logger.LogError("Failed to get Jellyseerr content from {Endpoint}. Status: {Status}", endpoint, contentResponse.StatusCode);
                        break;
                    }

                    var contentJsonStr = await contentResponse.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(contentJsonStr)) break;

                    var contentJson = JObject.Parse(contentJsonStr);
                    var items = contentJson.Value<JArray>("results");

                    if (items == null) break;

                    foreach (JObject item in items.OfType<JObject>().Where(x => !x.Value<bool>("adult")))
                    {
                        // Check preferred languages
                        if (!string.IsNullOrEmpty(Config.JellyseerrPreferredLanguages))
                        {
                            var langs = Config.JellyseerrPreferredLanguages.Split(',').Select(x => x.Trim());
                            var itemLang = item.Value<string>("originalLanguage");
                            if (itemLang != null && !langs.Contains(itemLang))
                            {
                                continue;
                            }
                        }

                        // We only want items that are NOT already in the library (mediaInfo == null usually means not available)
                        // But the logic in DiscoverSection checked if mediaInfo was null. 
                        // If mediaInfo is NOT null, it means it might be in Jellyfin/Plex already. 
                        // The original code: if (item.Value<JObject>("mediaInfo") == null)
                        
                        if (item.Value<JObject>("mediaInfo") == null)
                        {
                             string dateTimeString = item.Value<string>("firstAirDate") ??
                                                    item.Value<string>("releaseDate") ?? "1970-01-01";
                            
                            if (string.IsNullOrWhiteSpace(dateTimeString))
                            {
                                dateTimeString = "1970-01-01";
                            }
                            
                            string posterPath = item.Value<string>("posterPath") ?? "";
                            string cachedImageUrl = "";
                            if (!string.IsNullOrEmpty(posterPath))
                            {
                                cachedImageUrl = ImageCacheHelper.GetCachedImageUrl(m_imageCacheService, $"https://image.tmdb.org/t/p/w600_and_h900_bestv2{posterPath}");
                            }
                            
                            var id = item.Value<int>("id");
                            var mediaType = item.Value<string>("mediaType") ?? "movie"; // Default to movie if missing? Or derive?
                            // For TV endpoints, mediaType might be missing in results if it's strictly TV endpoint? 
                            // API v1 discover/tv usually implies TV. 
                            
                            // Generate a stable Hash ID for Jellyfin to use
                            var stableIdStr = $"jellyseerr_{mediaType}_{id}";
                            var stableId = GetStableHash(stableIdStr);

                            results.Add(new BaseItemDto()
                            {
                                Id = stableId,
                                Name = item.Value<string>("title") ?? item.Value<string>("name"),
                                OriginalTitle = item.Value<string>("originalTitle") ?? item.Value<string>("originalName"),
                                SourceType = mediaType,
                                ProviderIds = new Dictionary<string, string>()
                                {
                                    { "JellyseerrRoot", jellyseerrDisplayUrl ?? "" },
                                    { "Jellyseerr", id.ToString() },
                                    { "JellyseerrPoster", cachedImageUrl }
                                },
                                PremiereDate = DateTime.TryParse(dateTimeString, out var date) ? date : DateTime.MinValue,
                                Type = mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase) ? "Series" : "Movie" // Hint for client
                            });
                        }
                    }
                    
                    page++;
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error fetching Jellyseerr content.");
            }

            return results;
        }

        private Guid GetStableHash(string str)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(System.Text.Encoding.Default.GetBytes(str));
                return new Guid(hash);
            }
        }

        public async Task<T[]?> GetArrCalendarAsync<T>(ArrServiceType serviceType, DateTime startDate, DateTime endDate)
        {
            (string? url, string? apiKey, string? serviceName) = GetServiceConfig(serviceType);
            
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey))
            {
                m_logger.LogWarning("{ServiceName} URL or API key not configured", serviceName);
                return null;
            }

            try
            {
                string startParam = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string endParam = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
                (string? queryParams, string? apiVersion) = serviceType switch
                {
                    ArrServiceType.Sonarr => ($"includeSeries=true&start={startParam}&end={endParam}", "v3"),
                    ArrServiceType.Radarr => ($"start={startParam}&end={endParam}", "v3"),
                    ArrServiceType.Lidarr => ($"start={startParam}&end={endParam}", "v1"),
                    ArrServiceType.Readarr => ($"includeAuthor=true&start={startParam}&end={endParam}", "v1"),
                    _ => ($"start={startParam}&end={endParam}", "v3")
                };
                string requestUrl = $"{url.TrimEnd('/')}/api/{apiVersion}/calendar?{queryParams}";

                using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
                request.Headers.Add("X-API-KEY", apiKey);

                m_logger.LogDebug("Fetching {ServiceName} calendar from {Url}", serviceName, requestUrl);

                HttpResponseMessage response = await m_httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    m_logger.LogError("Failed to fetch {ServiceName} calendar. Status: {StatusCode}, Reason: {ReasonPhrase}", 
                        serviceName, response.StatusCode, response.ReasonPhrase);
                    return null;
                }

                string jsonContent = await response.Content.ReadAsStringAsync();
                
                if (string.IsNullOrEmpty(jsonContent))
                {
                    m_logger.LogWarning("Empty response from {ServiceName} calendar API", serviceName);
                    return [];
                }

                T[]? calendarItems = JsonSerializer.Deserialize<T[]>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                m_logger.LogDebug("Successfully fetched {Count} calendar items from {ServiceName}", calendarItems?.Length ?? 0, serviceName);
                return calendarItems ?? [];
            }
            catch (HttpRequestException ex)
            {
                m_logger.LogError(ex, "HTTP error while fetching {ServiceName} calendar", serviceName);
                return null;
            }
            catch (JsonException ex)
            {
                m_logger.LogError(ex, "JSON parsing error while processing {ServiceName} calendar response", serviceName);
                return null;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Unexpected error while fetching {ServiceName} calendar", serviceName);
                return null;
            }
        }

        private static (string? url, string? apiKey, string serviceName) GetServiceConfig(ArrServiceType serviceType)
        {
            return serviceType switch
            {
                ArrServiceType.Sonarr => (Config.Sonarr.Url, Config.Sonarr.ApiKey, "Sonarr"),
                ArrServiceType.Radarr => (Config.Radarr.Url, Config.Radarr.ApiKey, "Radarr"),
                ArrServiceType.Lidarr => (Config.Lidarr.Url, Config.Lidarr.ApiKey, "Lidarr"),
                ArrServiceType.Readarr => (Config.Readarr.Url, Config.Readarr.ApiKey, "Readarr"),
                _ => throw new ArgumentOutOfRangeException(nameof(serviceType), serviceType, "Unsupported service type")
            };
        }

        public static DateTime CalculateEndDate(DateTime startDate, int timeframeValue, TimeframeUnit timeframeUnit)
        {
            return timeframeUnit switch
            {
                TimeframeUnit.Days => startDate.AddDays(timeframeValue),
                TimeframeUnit.Weeks => startDate.AddDays(timeframeValue * 7),
                TimeframeUnit.Months => startDate.AddMonths(timeframeValue),
                TimeframeUnit.Years => startDate.AddYears(timeframeValue),
                _ => startDate.AddDays(timeframeValue)
            };
        }

        public static string FormatDate(DateTime date, string format, string delimiter)
        {
            return format.ToUpperInvariant() switch
            {
                "YYYY/MM/DD" => date.ToString($"yyyy{delimiter}MM{delimiter}dd"),
                "DD/MM/YYYY" => date.ToString($"dd{delimiter}MM{delimiter}yyyy"),
                "MM/DD/YYYY" => date.ToString($"MM{delimiter}dd{delimiter}yyyy"),
                "DD/MM" => date.ToString($"dd{delimiter}MM"),
                "MM/DD" => date.ToString($"MM{delimiter}dd"),
                _ => date.ToString($"yyyy{delimiter}MM{delimiter}dd")
            };
        }
    }
}