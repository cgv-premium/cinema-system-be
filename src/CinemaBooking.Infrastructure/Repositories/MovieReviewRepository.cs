using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Reviews;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Repositories;

public sealed class MovieReviewRepository : IMovieReviewRepository
{
    private readonly CinemaBookingDbContext _db;

    public MovieReviewRepository(CinemaBookingDbContext db)
    {
        _db = db;
    }

    public async Task<MovieReview> AddAsync(MovieReview review, CancellationToken cancellationToken = default)
    {
        await _db.MovieReviews.AddAsync(review, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<MovieReview?> GetByIdAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        return await _db.MovieReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReviewId == reviewId, cancellationToken);
    }

    public async Task<bool> ExistsAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        return await _db.MovieReviews
            .AnyAsync(r => r.ReviewId == reviewId, cancellationToken);
    }

    public async Task<bool> BookingHasReviewAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        return await _db.MovieReviews
            .AnyAsync(r => r.BookingId == bookingId, cancellationToken);
    }

    public async Task<bool> UserHasReviewedMovieAsync(int userId, int movieId, CancellationToken cancellationToken = default)
    {
        return await _db.MovieReviews
            .AnyAsync(r => r.UserId == userId && r.MovieId == movieId, cancellationToken);
    }

    public async Task<bool> UserHasAnyReviewAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _db.MovieReviews
            .AnyAsync(r => r.UserId == userId, cancellationToken);
    }

    public async Task<bool> MovieExistsAsync(int movieId, CancellationToken cancellationToken = default)
    {
        return await _db.Movie
            .AnyAsync(m => m.MovieID == movieId, cancellationToken);
    }

    public async Task<MovieReview?> GetForUpdateAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        return await _db.MovieReviews
            .FirstOrDefaultAsync(r => r.ReviewId == reviewId, cancellationToken);
    }

    public async Task UpdateAsync(MovieReview review, CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MovieReviewStats> GetVisibleStatsForMovieAsync(
        int movieId,
        CancellationToken cancellationToken = default)
    {
        var buckets = await _db.MovieReviews
            .AsNoTracking()
            .Where(r => r.MovieId == movieId && !r.IsHidden)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var breakdown = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };
        var total = 0;
        var weightedSum = 0L;

        foreach (var bucket in buckets)
        {
            if (breakdown.ContainsKey(bucket.Rating))
            {
                breakdown[bucket.Rating] = bucket.Count;
            }
            total += bucket.Count;
            weightedSum += (long)bucket.Rating * bucket.Count;
        }

        if (total == 0)
        {
            return new MovieReviewStats(null, 0, breakdown);
        }

        var average = Math.Round(weightedSum / (double)total, 1);
        return new MovieReviewStats(average, total, breakdown);
    }

    public async Task<IReadOnlyDictionary<int, MovieReviewStats>> GetVisibleStatsForMoviesAsync(
        IReadOnlyCollection<int> movieIds,
        CancellationToken cancellationToken = default)
    {
        if (movieIds.Count == 0)
            return new Dictionary<int, MovieReviewStats>();

        var buckets = await _db.MovieReviews
            .AsNoTracking()
            .Where(r => movieIds.Contains(r.MovieId) && !r.IsHidden)
            .GroupBy(r => new { r.MovieId, r.Rating })
            .Select(g => new { g.Key.MovieId, g.Key.Rating, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var grouped = buckets.GroupBy(b => b.MovieId);
        var result = new Dictionary<int, MovieReviewStats>();

        foreach (var group in grouped)
        {
            var breakdown = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };
            var total = 0;
            var weightedSum = 0L;

            foreach (var bucket in group)
            {
                if (breakdown.ContainsKey(bucket.Rating))
                    breakdown[bucket.Rating] = bucket.Count;
                total += bucket.Count;
                weightedSum += (long)bucket.Rating * bucket.Count;
            }

            double? average = total == 0 ? null : Math.Round(weightedSum / (double)total, 1);
            result[group.Key] = new MovieReviewStats(average, total, breakdown);
        }

        foreach (var movieId in movieIds)
        {
            if (!result.ContainsKey(movieId))
                result[movieId] = new MovieReviewStats(null, 0, new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } });
        }

        return result;
    }

    public async Task<List<ReviewListItem>> GetVisibleReviewsForMovieAsync(
        int movieId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var skip = (page - 1) * pageSize;

        return await _db.MovieReviews
            .AsNoTracking()
            .Where(r => r.MovieId == movieId && !r.IsHidden)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ReviewId)
            .Skip(skip)
            .Take(pageSize)
            .Select(r => new ReviewListItem(
                r.ReviewId,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                r.UserId,
                r.User!.FullName,
                r.User!.AvatarURL))
            .ToListAsync(cancellationToken);
    }

    public async Task<(int? ReviewId, bool HasReview)> GetBookingReviewLookupAsync(
        int bookingId,
        CancellationToken cancellationToken = default)
    {
        var reviewId = await _db.MovieReviews
            .AsNoTracking()
            .Where(r => r.BookingId == bookingId)
            .Select(r => (int?)r.ReviewId)
            .FirstOrDefaultAsync(cancellationToken);

        return (reviewId, reviewId.HasValue);
    }

    public async Task<IReadOnlyDictionary<int, int>> GetReviewIdsByBookingIdsAsync(
        IReadOnlyCollection<int> bookingIds,
        CancellationToken cancellationToken = default)
    {
        if (bookingIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var rows = await _db.MovieReviews
            .AsNoTracking()
            .Where(r => bookingIds.Contains(r.BookingId))
            .Select(r => new { r.BookingId, r.ReviewId })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.BookingId, x => x.ReviewId);
    }

    public async Task<(IReadOnlyList<AdminReviewListItem> Items, int Total)> SearchAdminReviewsAsync(
        string? keyword,
        int? movieId,
        AdminReviewStatusFilter status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.MovieReviews.AsNoTracking().AsQueryable();

        if (status == AdminReviewStatusFilter.Active)
        {
            query = query.Where(r => !r.IsHidden);
        }
        else if (status == AdminReviewStatusFilter.Hidden)
        {
            query = query.Where(r => r.IsHidden);
        }

        if (movieId.HasValue)
        {
            query = query.Where(r => r.MovieId == movieId.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            query = query.Where(r =>
                EF.Functions.Like(r.User!.FullName, pattern)
                || EF.Functions.Like(r.Movie!.Title, pattern));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ReviewId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminReviewListItem(
                r.ReviewId,
                r.MovieId,
                r.Movie!.Title,
                r.UserId,
                r.User!.FullName,
                r.User!.AvatarURL,
                r.Rating,
                r.Comment,
                r.IsHidden,
                r.CreatedAt,
                r.HiddenAt))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<MovieReviewDashboardItem> Items, int TotalItems)> GetMovieReviewDashboardAsync(
        string? searchTitle,
        DateTime? fromUtc,
        DateTime? toUtc,
        double? minAverageRating,
        double? maxAverageRating,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var reviewQuery = _db.MovieReviews.AsNoTracking().Where(r => !r.IsHidden);
        if (fromUtc.HasValue)
        {
            reviewQuery = reviewQuery.Where(r => r.CreatedAt >= fromUtc.Value);
        }
        if (toUtc.HasValue)
        {
            reviewQuery = reviewQuery.Where(r => r.CreatedAt < toUtc.Value);
        }

        var moviesQuery = _db.Movie.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(searchTitle))
        {
            var pattern = $"%{searchTitle.Trim()}%";
            moviesQuery = moviesQuery.Where(m => EF.Functions.Like(m.Title, pattern));
        }

        var intermediate = moviesQuery
            .Select(m => new
            {
                MovieId = m.MovieID,
                MovieTitle = m.Title,
                PosterUrl = m.PosterURL,
                TotalReviews = reviewQuery.Count(r => r.MovieId == m.MovieID),
                AverageRating = reviewQuery.Where(r => r.MovieId == m.MovieID).Any()
                    ? (double?)reviewQuery.Where(r => r.MovieId == m.MovieID).Average(r => (double)r.Rating)
                    : null,
                FiveStarCount = reviewQuery.Count(r => r.MovieId == m.MovieID && r.Rating == 5),
                FourStarCount = reviewQuery.Count(r => r.MovieId == m.MovieID && r.Rating == 4),
                ThreeStarCount = reviewQuery.Count(r => r.MovieId == m.MovieID && r.Rating == 3),
                TwoStarCount = reviewQuery.Count(r => r.MovieId == m.MovieID && r.Rating == 2),
                OneStarCount = reviewQuery.Count(r => r.MovieId == m.MovieID && r.Rating == 1),
                LatestReviewDate = reviewQuery.Where(r => r.MovieId == m.MovieID).Any()
                    ? (DateTime?)reviewQuery.Where(r => r.MovieId == m.MovieID).Max(r => r.CreatedAt)
                    : null
            });

        if (minAverageRating.HasValue)
        {
            intermediate = intermediate.Where(x => x.AverageRating.HasValue && x.AverageRating.Value >= minAverageRating.Value);
        }
        if (maxAverageRating.HasValue)
        {
            intermediate = intermediate.Where(x => x.AverageRating.HasValue && x.AverageRating.Value <= maxAverageRating.Value);
        }

        intermediate = (sortBy, descending) switch
        {
            ("newestreview", true) => intermediate.OrderByDescending(x => x.LatestReviewDate).ThenByDescending(x => x.MovieId),
            ("newestreview", false) => intermediate.OrderBy(x => x.LatestReviewDate).ThenBy(x => x.MovieId),
            ("highestrating", _) => intermediate.OrderByDescending(x => x.AverageRating).ThenByDescending(x => x.TotalReviews).ThenBy(x => x.MovieId),
            ("lowestrating", _) => intermediate.OrderBy(x => x.AverageRating).ThenByDescending(x => x.TotalReviews).ThenBy(x => x.MovieId),
            ("mostreviews", true) => intermediate.OrderByDescending(x => x.TotalReviews).ThenByDescending(x => x.MovieId),
            ("mostreviews", false) => intermediate.OrderBy(x => x.TotalReviews).ThenBy(x => x.MovieId),
            ("moviename", true) => intermediate.OrderByDescending(x => x.MovieTitle).ThenBy(x => x.MovieId),
            ("moviename", false) => intermediate.OrderBy(x => x.MovieTitle).ThenBy(x => x.MovieId),
            _ => intermediate.OrderByDescending(x => x.LatestReviewDate).ThenByDescending(x => x.MovieId)
        };

        var totalItems = await intermediate.CountAsync(cancellationToken);

        var rawItems = await intermediate
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rawItems.ConvertAll(x => new MovieReviewDashboardItem(
            x.MovieId,
            x.MovieTitle,
            x.PosterUrl,
            x.TotalReviews,
            x.AverageRating.HasValue ? Math.Round(x.AverageRating.Value, 1) : null,
            x.FiveStarCount,
            x.FourStarCount,
            x.ThreeStarCount,
            x.TwoStarCount,
            x.OneStarCount,
            x.LatestReviewDate));

        return (items, totalItems);
    }

    public async Task<MovieReviewStats> GetMovieReviewStatsByDateRangeAsync(
        int movieId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? minRating,
        int? maxRating,
        CancellationToken cancellationToken = default)
    {
        var query = _db.MovieReviews.AsNoTracking().Where(r => r.MovieId == movieId && !r.IsHidden);
        if (fromUtc.HasValue) query = query.Where(r => r.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(r => r.CreatedAt < toUtc.Value);
        if (minRating.HasValue) query = query.Where(r => r.Rating >= minRating.Value);
        if (maxRating.HasValue) query = query.Where(r => r.Rating <= maxRating.Value);

        var buckets = await query
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var breakdown = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };
        var total = 0;
        var weightedSum = 0L;

        foreach (var bucket in buckets)
        {
            if (breakdown.ContainsKey(bucket.Rating))
            {
                breakdown[bucket.Rating] = bucket.Count;
            }
            total += bucket.Count;
            weightedSum += (long)bucket.Rating * bucket.Count;
        }

        if (total == 0)
        {
            return new MovieReviewStats(null, 0, breakdown);
        }

        var average = Math.Round(weightedSum / (double)total, 1);
        return new MovieReviewStats(average, total, breakdown);
    }

    public async Task<(IReadOnlyList<ReviewDetailItem> Items, int TotalItems)> GetMovieReviewsDetailedByDateRangeAsync(
        int movieId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? minRating,
        int? maxRating,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.MovieReviews.AsNoTracking().Where(r => r.MovieId == movieId && !r.IsHidden);
        if (fromUtc.HasValue) query = query.Where(r => r.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(r => r.CreatedAt < toUtc.Value);
        if (minRating.HasValue) query = query.Where(r => r.Rating >= minRating.Value);
        if (maxRating.HasValue) query = query.Where(r => r.Rating <= maxRating.Value);

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ReviewId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReviewDetailItem(
                r.ReviewId,
                r.UserId,
                r.User!.FullName,
                r.User!.AvatarURL,
                r.BookingId,
                r.CreatedAt,
                r.Rating,
                r.Comment))
            .ToListAsync(cancellationToken);

        return (items, totalItems);
    }
}
