namespace CinemaBooking.Application.Reviews;

public interface IMovieReviewReportService
{
    Task<GetMovieReviewDashboardResult> GetDashboardAsync(
        string? searchTitle,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        double? minAverageRating,
        double? maxAverageRating,
        string? sortBy,
        string? sortDir,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<GetMovieReviewDetailResult> GetDetailAsync(
        int movieId,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        int? minRating,
        int? maxRating,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed record GetMovieReviewDashboardResult(
    bool Succeeded,
    string? ErrorMessage,
    MovieReviewDashboardPage? Data,
    string? ErrorCode = null);

public sealed record GetMovieReviewDetailResult(
    bool Succeeded,
    string? ErrorMessage,
    MovieReviewDetailResponse? Data,
    string? ErrorCode = null);
