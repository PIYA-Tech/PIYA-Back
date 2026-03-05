using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing pharmacy ratings and reviews
/// </summary>
public interface IPharmacyRatingService
{
    /// <summary>
    /// Add or update a rating for a pharmacy
    /// </summary>
    Task<PharmacyRating> AddOrUpdateRatingAsync(Guid userId, Guid pharmacyId, int rating, string? reviewText, 
        PharmacyRatingCategories? categories, bool? wouldRecommend, Guid? prescriptionId = null);
    
    /// <summary>
    /// Get a specific rating by ID
    /// </summary>
    Task<PharmacyRating?> GetRatingByIdAsync(Guid ratingId);
    
    /// <summary>
    /// Get all ratings for a pharmacy
    /// </summary>
    Task<List<PharmacyRating>> GetPharmacyRatingsAsync(Guid pharmacyId, int skip = 0, int take = 20);
    
    /// <summary>
    /// Get ratings by a specific user
    /// </summary>
    Task<List<PharmacyRating>> GetUserRatingsAsync(Guid userId);
    
    /// <summary>
    /// Get user's rating for a specific pharmacy
    /// </summary>
    Task<PharmacyRating?> GetUserPharmacyRatingAsync(Guid userId, Guid pharmacyId);
    
    /// <summary>
    /// Delete a rating
    /// </summary>
    Task<bool> DeleteRatingAsync(Guid ratingId, Guid userId);
    
    /// <summary>
    /// Get pharmacy rating statistics
    /// </summary>
    Task<PharmacyRatingStats> GetPharmacyRatingStatsAsync(Guid pharmacyId);
    
    /// <summary>
    /// Recalculate pharmacy average rating
    /// </summary>
    Task RecalculatePharmacyRatingAsync(Guid pharmacyId);
}

/// <summary>
/// Pharmacy rating statistics
/// </summary>
public class PharmacyRatingStats
{
    public decimal AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = [];
    public int VerifiedRatings { get; set; }
    public int RecommendCount { get; set; }
    public PharmacyRatingCategoryAverages? CategoryAverages { get; set; }
}

/// <summary>
/// Average ratings for categories
/// </summary>
public class PharmacyRatingCategoryAverages
{
    public decimal? ServiceQuality { get; set; }
    public decimal? StaffFriendliness { get; set; }
    public decimal? WaitTime { get; set; }
    public decimal? ProductAvailability { get; set; }
    public decimal? Cleanliness { get; set; }
    public decimal? PriceValue { get; set; }
}
