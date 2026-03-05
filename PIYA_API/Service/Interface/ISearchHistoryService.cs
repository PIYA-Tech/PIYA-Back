using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for tracking and analyzing user search history
/// </summary>
public interface ISearchHistoryService
{
    /// <summary>
    /// Log a search action
    /// </summary>
    Task<SearchHistory> LogSearchAsync(Guid userId, SearchType searchType, string? searchQuery, 
        string? filters, int resultCount, Guid? coordinatesId = null);
    
    /// <summary>
    /// Record which result the user selected
    /// </summary>
    Task RecordResultSelectionAsync(Guid searchHistoryId, Guid selectedResultId, string selectedResultType);
    
    /// <summary>
    /// Get user's search history
    /// </summary>
    Task<List<SearchHistory>> GetUserSearchHistoryAsync(Guid userId, int skip = 0, int take = 50);
    
    /// <summary>
    /// Get user's recent searches by type
    /// </summary>
    Task<List<SearchHistory>> GetRecentSearchesByTypeAsync(Guid userId, SearchType searchType, int limit = 10);
    
    /// <summary>
    /// Get user's popular search queries
    /// </summary>
    Task<List<string>> GetPopularSearchQueriesAsync(Guid userId, SearchType? searchType = null, int limit = 10);
    
    /// <summary>
    /// Clear user's search history
    /// </summary>
    Task<int> ClearSearchHistoryAsync(Guid userId, DateTime? olderThan = null);
    
    /// <summary>
    /// Get search analytics for a user
    /// </summary>
    Task<UserSearchAnalytics> GetUserSearchAnalyticsAsync(Guid userId);
}

/// <summary>
/// User search analytics
/// </summary>
public class UserSearchAnalytics
{
    public int TotalSearches { get; set; }
    public Dictionary<SearchType, int> SearchesByType { get; set; } = [];
    public List<string> MostSearchedQueries { get; set; } = [];
    public Dictionary<string, int> SelectionRate { get; set; } = [];
    public DateTime? LastSearchAt { get; set; }
}
