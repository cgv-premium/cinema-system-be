using CinemaBooking.Application.Reviews;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Application.Common.Interfaces;

public interface IMovieReviewRepository
{
    Task<MovieReview> AddAsync(MovieReview review, CancellationToken cancellationToken = default);
    Task<MovieReview?> GetByIdAsync(int reviewId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int reviewId, CancellationToken cancellationToken = default);

    Task<bool> BookingHasReviewAsync(int bookingId, CancellationToken cancellationToken = default);
    Task<bool> UserHasReviewedMovieAsync(int userId, int movieId, CancellationToken cancellationToken = default);
    Task<bool> UserHasAnyReviewAsync(int userId, CancellationToken cancellationToken = default);

    Task<bool> MovieExistsAsync(int movieId, CancellationToken cancellationToken = default);

    Task<MovieReview?> GetForUpdateAsync(int reviewId, CancellationToken cancellationToken = default);

    Task UpdateAsync(MovieReview review, CancellationToken cancellationToken = default);

    Task<MovieReviewStats> GetVisibleStatsForMovieAsync(
        int movieId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, MovieReviewStats>> GetVisibleStatsForMoviesAsync(
        IReadOnlyCollection<int> movieIds,
        CancellationToken cancellationToken = default);

    Task<List<ReviewListItem>> GetVisibleReviewsForMovieAsync(
        int movieId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(int? ReviewId, bool HasReview)> GetBookingReviewLookupAsync(
        int bookingId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, int>> GetReviewIdsByBookingIdsAsync(
        IReadOnlyCollection<int> bookingIds,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AdminReviewListItem> Items, int Total)> SearchAdminReviewsAsync(
        string? keyword,
        int? movieId,
        AdminReviewStatusFilter status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MovieReviewDashboardItem> Items, int TotalItems)> GetMovieReviewDashboardAsync(
        string? searchTitle,
        DateTime? fromUtc,
        DateTime? toUtc,
        double? minAverageRating,
        double? maxAverageRating,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<MovieReviewStats> GetMovieReviewStatsByDateRangeAsync(
        int movieId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? minRating,
        int? maxRating,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ReviewDetailItem> Items, int TotalItems)> GetMovieReviewsDetailedByDateRangeAsync(
        int movieId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? minRating,
        int? maxRating,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
