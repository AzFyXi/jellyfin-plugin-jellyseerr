using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class DiscoverSection : IHomeScreenSection
    {
        private readonly IUserManager m_userManager;
        private readonly ImageCacheService m_imageCacheService;
        private readonly ArrApiService m_arrApiService;
        private readonly ILogger<DiscoverSection> m_logger;
        
        public virtual string? Section => "Discover";

        public virtual string? DisplayText { get; set; } = "Discover";
        public int? Limit => 1;
        public string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload { get; } = null;

        protected virtual string JellyseerEndpoint => "/api/v1/discover/trending";
        
        public DiscoverSection(IUserManager userManager, ImageCacheService imageCacheService, ArrApiService arrApiService, ILogger<DiscoverSection> logger)
        {
            m_userManager = userManager;
            m_imageCacheService = imageCacheService;
            m_arrApiService = arrApiService;
            m_logger = logger;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = m_userManager.GetUserById(payload.UserId);
            if (user == null)
            {
                m_logger.LogWarning("User with Id {UserId} not found.", payload.UserId);
                return new QueryResult<BaseItemDto>();
            }

            try
            {
                // Synchronously wait for the async task since the interface requires synchronous return.
                // Ideally, this should be refactored to support async if possible, but given the constraints, this works.
                List<BaseItemDto> returnItems = m_arrApiService.GetJellyseerrContentAsync(JellyseerEndpoint, user.Username).GetAwaiter().GetResult();
                
                return new QueryResult<BaseItemDto>()
                {
                    Items = returnItems,
                    StartIndex = 0,
                    TotalRecordCount = returnItems.Count
                };
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error getting results for DiscoverSection.");
                return new QueryResult<BaseItemDto>();
            }
        }

        protected string GetCachedImageUrl(string sourceUrl)
        {
            return ImageCacheHelper.GetCachedImageUrl(m_imageCacheService, sourceUrl);
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return this;
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo()
            {
                Section = Section,
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = SectionViewMode.Portrait,
                AllowViewModeChange = false
            };
        }
    }
}
