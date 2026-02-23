using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class SearchHistoryService(PharmacyApiDbContext context) : ISearchHistoryService
{
    private readonly PharmacyApiDbContext _context = context;

    public async Task<SearchHistory> LogSearchAsync(Guid userId, SearchType searchType, string? searchQuery, 
        string? filters, int resultCount, Guid? coordinatesId = null)
    {
        var searchHistory = new SearchHistory
        {
            UserId = userId,
            SearchType = searchType,
            SearchQuery = searchQuery,
            Filters = filters,
            ResultCount = resultCount,
            CoordinatesId = coordinatesId,
            SearchedAt = DateTime.UtcNow
        };

        _context.SearchHistories.Add(searchHistory);
        await _context.SaveChangesAsync();

        return searchHistory;
    }

    public async Task RecordResultSelectionAsync(Guid searchHistoryId, Guid selectedResultId, string selectedResultType)
    {
        var searchHistory = await _context.SearchHistories.FindAsync(searchHistoryId);
        if (searchHistory != null)
        {
            searchHistory.SelectedResultId = selectedResultId;
            searchHistory.SelectedResultType = selectedResultType;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<SearchHistory>> GetUserSearchHistoryAsync(Guid userId, int skip = 0, int take = 50)
    {
        return await _context.SearchHistories
            .Where(sh => sh.UserId == userId)
            .OrderByDescending(sh => sh.SearchedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<SearchHistory>> GetRecentSearchesByTypeAsync(Guid userId, SearchType searchType, int limit = 10)
    {
        return await _context.SearchHistories
            .Where(sh => sh.UserId == userId && sh.SearchType == searchType)
            .OrderByDescending(sh => sh.SearchedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<string>> GetPopularSearchQueriesAsync(Guid userId, SearchType? searchType = null, int limit = 10)
    {
        var query = _context.SearchHistories
            .Where(sh => sh.UserId == userId && !string.IsNullOrEmpty(sh.SearchQuery));

        if (searchType.HasValue)
        {
            query = query.Where(sh => sh.SearchType == searchType.Value);
        }

        var popularQueries = await query
            .GroupBy(sh => sh.SearchQuery)
            .Select(g => new { Query = g.Key!, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .Select(x => x.Query)
            .ToListAsync();

        return popularQueries;
    }

    public async Task<int> ClearSearchHistoryAsync(Guid userId, DateTime? olderThan = null)
    {
        var query = _context.SearchHistories.Where(sh => sh.UserId == userId);

        if (olderThan.HasValue)
        {
            query = query.Where(sh => sh.SearchedAt < olderThan.Value);
        }

        var toDelete = await query.ToListAsync();
        _context.SearchHistories.RemoveRange(toDelete);
        await _context.SaveChangesAsync();

        return toDelete.Count;
    }

    public async Task<UserSearchAnalytics> GetUserSearchAnalyticsAsync(Guid userId)
    {
        var searches = await _context.SearchHistories
            .Where(sh => sh.UserId == userId)
            .ToListAsync();

        if (!searches.Any())
        {
            return new UserSearchAnalytics
            {
                TotalSearches = 0
            };
        }

        var analytics = new UserSearchAnalytics
        {
            TotalSearches = searches.Count,
            SearchesByType = searches.GroupBy(sh => sh.SearchType)
                .ToDictionary(g => g.Key, g => g.Count()),
            LastSearchAt = searches.Max(sh => sh.SearchedAt)
        };

        // Most searched queries
        analytics.MostSearchedQueries = searches
            .Where(sh => !string.IsNullOrEmpty(sh.SearchQuery))
            .GroupBy(sh => sh.SearchQuery)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key!)
            .ToList();

        // Selection rate by search type
        foreach (var searchType in Enum.GetValues<SearchType>())
        {
            var typeSearches = searches.Where(sh => sh.SearchType == searchType).ToList();
            if (typeSearches.Any())
            {
                var selectionRate = (int)((double)typeSearches.Count(sh => sh.SelectedResultId != null) / typeSearches.Count * 100);
                analytics.SelectionRate[searchType.ToString()] = selectionRate;
            }
        }

        return analytics;
    }
}
