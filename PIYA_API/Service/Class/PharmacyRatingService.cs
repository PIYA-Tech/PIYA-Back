using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PharmacyRatingService(PharmacyApiDbContext context, IAuditService auditService) : IPharmacyRatingService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;

    public async Task<PharmacyRating> AddOrUpdateRatingAsync(Guid userId, Guid pharmacyId, int rating, 
        string? reviewText, PharmacyRatingCategories? categories, bool? wouldRecommend, Guid? prescriptionId = null)
    {
        if (rating < 1 || rating > 5)
            throw new ArgumentException("Rating must be between 1 and 5", nameof(rating));

        // Check if user already rated this pharmacy
        var existingRating = await _context.PharmacyRatings
            .FirstOrDefaultAsync(r => r.UserId == userId && r.PharmacyId == pharmacyId);

        if (existingRating != null)
        {
            // Update existing rating
            existingRating.Rating = rating;
            existingRating.ReviewText = reviewText;
            existingRating.Categories = categories;
            existingRating.WouldRecommend = wouldRecommend;
            existingRating.UpdatedAt = DateTime.UtcNow;

            await _auditService.LogEntityActionAsync("Update", "PharmacyRating", existingRating.Id.ToString(), 
                userId, $"Updated rating for pharmacy {pharmacyId}");
        }
        else
        {
            // Create new rating
            var isVerified = false;
            if (prescriptionId.HasValue)
            {
                // Check if prescription exists and was fulfilled at this pharmacy
                var prescription = await _context.Prescriptions
                    .FirstOrDefaultAsync(p => p.Id == prescriptionId.Value && 
                                            p.FulfilledByPharmacyId == pharmacyId);
                isVerified = prescription != null;
            }

            existingRating = new PharmacyRating
            {
                PharmacyId = pharmacyId,
                UserId = userId,
                Rating = rating,
                ReviewText = reviewText,
                Categories = categories,
                WouldRecommend = wouldRecommend,
                PrescriptionId = prescriptionId,
                IsVerified = isVerified
            };

            _context.PharmacyRatings.Add(existingRating);

            await _auditService.LogEntityActionAsync("Create", "PharmacyRating", existingRating.Id.ToString(), 
                userId, $"Created rating for pharmacy {pharmacyId}");
        }

        await _context.SaveChangesAsync();
        
        // Recalculate pharmacy average rating
        await RecalculatePharmacyRatingAsync(pharmacyId);

        return existingRating;
    }

    public async Task<PharmacyRating?> GetRatingByIdAsync(Guid ratingId)
    {
        return await _context.PharmacyRatings
            .Include(r => r.User)
            .Include(r => r.Pharmacy)
            .FirstOrDefaultAsync(r => r.Id == ratingId);
    }

    public async Task<List<PharmacyRating>> GetPharmacyRatingsAsync(Guid pharmacyId, int skip = 0, int take = 20)
    {
        return await _context.PharmacyRatings
            .Where(r => r.PharmacyId == pharmacyId)
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<PharmacyRating>> GetUserRatingsAsync(Guid userId)
    {
        return await _context.PharmacyRatings
            .Where(r => r.UserId == userId)
            .Include(r => r.Pharmacy)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<PharmacyRating?> GetUserPharmacyRatingAsync(Guid userId, Guid pharmacyId)
    {
        return await _context.PharmacyRatings
            .FirstOrDefaultAsync(r => r.UserId == userId && r.PharmacyId == pharmacyId);
    }

    public async Task<bool> DeleteRatingAsync(Guid ratingId, Guid userId)
    {
        var rating = await _context.PharmacyRatings
            .FirstOrDefaultAsync(r => r.Id == ratingId && r.UserId == userId);

        if (rating == null)
            return false;

        var pharmacyId = rating.PharmacyId;

        _context.PharmacyRatings.Remove(rating);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync("Delete", "PharmacyRating", ratingId.ToString(), 
            userId, $"Deleted rating for pharmacy {pharmacyId}");

        // Recalculate pharmacy average rating
        await RecalculatePharmacyRatingAsync(pharmacyId);

        return true;
    }

    public async Task<PharmacyRatingStats> GetPharmacyRatingStatsAsync(Guid pharmacyId)
    {
        var ratings = await _context.PharmacyRatings
            .Where(r => r.PharmacyId == pharmacyId)
            .ToListAsync();

        if (ratings.Count == 0)
        {
            return new PharmacyRatingStats
            {
                AverageRating = 0,
                TotalRatings = 0,
                RatingDistribution = []
            };
        }

        var stats = new PharmacyRatingStats
        {
            AverageRating = (decimal)ratings.Average(r => r.Rating),
            TotalRatings = ratings.Count,
            RatingDistribution = ratings.GroupBy(r => r.Rating)
                .ToDictionary(g => g.Key, g => g.Count()),
            VerifiedRatings = ratings.Count(r => r.IsVerified),
            RecommendCount = ratings.Count(r => r.WouldRecommend == true)
        };

        // Calculate category averages
        var ratingsWithCategories = ratings.Where(r => r.Categories != null).ToList();
        if (ratingsWithCategories.Any())
        {
            stats.CategoryAverages = new PharmacyRatingCategoryAverages
            {
                ServiceQuality = ratingsWithCategories
                    .Where(r => r.Categories!.ServiceQuality.HasValue)
                    .Average(r => (decimal?)r.Categories!.ServiceQuality),
                StaffFriendliness = ratingsWithCategories
                    .Where(r => r.Categories!.StaffFriendliness.HasValue)
                    .Average(r => (decimal?)r.Categories!.StaffFriendliness),
                WaitTime = ratingsWithCategories
                    .Where(r => r.Categories!.WaitTime.HasValue)
                    .Average(r => (decimal?)r.Categories!.WaitTime),
                ProductAvailability = ratingsWithCategories
                    .Where(r => r.Categories!.ProductAvailability.HasValue)
                    .Average(r => (decimal?)r.Categories!.ProductAvailability),
                Cleanliness = ratingsWithCategories
                    .Where(r => r.Categories!.Cleanliness.HasValue)
                    .Average(r => (decimal?)r.Categories!.Cleanliness),
                PriceValue = ratingsWithCategories
                    .Where(r => r.Categories!.PriceValue.HasValue)
                    .Average(r => (decimal?)r.Categories!.PriceValue)
            };
        }

        return stats;
    }

    public async Task RecalculatePharmacyRatingAsync(Guid pharmacyId)
    {
        var pharmacy = await _context.Pharmacies.FindAsync(pharmacyId);
        if (pharmacy == null)
            return;

        var ratings = await _context.PharmacyRatings
            .Where(r => r.PharmacyId == pharmacyId)
            .ToListAsync();

        if (ratings.Any())
        {
            pharmacy.AverageRating = (decimal)ratings.Average(r => r.Rating);
            pharmacy.TotalRatings = ratings.Count;
        }
        else
        {
            pharmacy.AverageRating = 0;
            pharmacy.TotalRatings = 0;
        }

        pharmacy.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }
}
