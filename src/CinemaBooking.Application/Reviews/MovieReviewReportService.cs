using CinemaBooking.Application.Common.Interfaces;

namespace CinemaBooking.Application.Reviews;

public sealed class MovieReviewReportService : IMovieReviewReportService
{
    private static readonly HashSet<string> AllowedSortBy = new(StringComparer.OrdinalIgnoreCase)
    {
        "newestReview",
        "highestRating",
        "lowestRating",
        "mostReviews",
        "movieName"
    };

    private readonly IMovieReviewRepository _reviewRepository;
    private readonly IMovieRepository _movieRepository;

    public MovieReviewReportService(
        IMovieReviewRepository reviewRepository,
        IMovieRepository movieRepository)
    {
        _reviewRepository = reviewRepository;
        _movieRepository = movieRepository;
    }

    public async Task<GetMovieReviewDashboardResult> GetDashboardAsync(
        string? searchTitle,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        double? minAverageRating,
        double? maxAverageRating,
        string? sortBy,
        string? sortDir,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => pageSize
        };

        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy) ? "newestReview" : sortBy.Trim();
        if (!AllowedSortBy.Contains(normalizedSortBy))
        {
            return new GetMovieReviewDashboardResult(
                false,
                "sortBy must be one of: newestReview, highestRating, lowestRating, mostReviews, movieName.",
                null,
                "invalid_input");
        }

        var descending = true;
        if (!string.IsNullOrWhiteSpace(sortDir))
        {
            var trimmed = sortDir.Trim();
            if (string.Equals(trimmed, "asc", StringComparison.OrdinalIgnoreCase))
            {
                descending = false;
            }
            else if (string.Equals(trimmed, "desc", StringComparison.OrdinalIgnoreCase))
            {
                descending = true;
            }
            else
            {
                return new GetMovieReviewDashboardResult(
                    false,
                    "sortDir must be asc or desc.",
                    null,
                    "invalid_input");
            }
        }
        else
        {
            descending = normalizedSortBy switch
            {
                "lowestRating" => false,
                "movieName" => false,
                _ => true
            };
        }

        if (fromDateUtc.HasValue && toDateUtc.HasValue && fromDateUtc.Value > toDateUtc.Value)
        {
            return new GetMovieReviewDashboardResult(
                false,
                "fromDate must not be after toDate.",
                null,
                "invalid_input");
        }

        if (minAverageRating.HasValue && (minAverageRating.Value < 1 || minAverageRating.Value > 5))
        {
            return new GetMovieReviewDashboardResult(
                false,
                "minAverageRating must be between 1 and 5.",
                null,
                "invalid_input");
        }

        if (maxAverageRating.HasValue && (maxAverageRating.Value < 1 || maxAverageRating.Value > 5))
        {
            return new GetMovieReviewDashboardResult(
                false,
                "maxAverageRating must be between 1 and 5.",
                null,
                "invalid_input");
        }

        if (minAverageRating.HasValue && maxAverageRating.HasValue
            && minAverageRating.Value > maxAverageRating.Value)
        {
            return new GetMovieReviewDashboardResult(
                false,
                "minAverageRating must not exceed maxAverageRating.",
                null,
                "invalid_input");
        }

        var (items, totalItems) = await _reviewRepository.GetMovieReviewDashboardAsync(
            searchTitle?.Trim(),
            fromDateUtc,
            toDateUtc,
            minAverageRating,
            maxAverageRating,
            normalizedSortBy.ToLowerInvariant(),
            descending,
            normalizedPage,
            normalizedPageSize,
            cancellationToken);

        var totalPages = normalizedPageSize == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)normalizedPageSize);

        var page1 = new MovieReviewDashboardPage(
            items,
            normalizedPage,
            normalizedPageSize,
            totalItems,
            totalPages);

        return new GetMovieReviewDashboardResult(true, null, page1);
    }

    public async Task<GetMovieReviewDetailResult> GetDetailAsync(
        int movieId,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        int? minRating,
        int? maxRating,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => pageSize
        };

        if (fromDateUtc.HasValue && toDateUtc.HasValue && fromDateUtc.Value > toDateUtc.Value)
        {
            return new GetMovieReviewDetailResult(
                false,
                "fromDate must not be after toDate.",
                null,
                "invalid_input");
        }

        if (minRating.HasValue && (minRating.Value < 1 || minRating.Value > 5))
        {
            return new GetMovieReviewDetailResult(
                false,
                "minRating must be between 1 and 5.",
                null,
                "invalid_input");
        }

        if (maxRating.HasValue && (maxRating.Value < 1 || maxRating.Value > 5))
        {
            return new GetMovieReviewDetailResult(
                false,
                "maxRating must be between 1 and 5.",
                null,
                "invalid_input");
        }

        if (minRating.HasValue && maxRating.HasValue && minRating.Value > maxRating.Value)
        {
            return new GetMovieReviewDetailResult(
                false,
                "minRating must not exceed maxRating.",
                null,
                "invalid_input");
        }

        var movie = await _movieRepository.GetByIdAsync(movieId, cancellationToken);
        if (movie is null)
        {
            return new GetMovieReviewDetailResult(false, "Movie not found.", null, "not_found");
        }

        var stats = await _reviewRepository.GetMovieReviewStatsByDateRangeAsync(
            movieId,
            fromDateUtc,
            toDateUtc,
            minRating,
            maxRating,
            cancellationToken);

        var (items, totalItems) = await _reviewRepository.GetMovieReviewsDetailedByDateRangeAsync(
            movieId,
            fromDateUtc,
            toDateUtc,
            minRating,
            maxRating,
            normalizedPage,
            normalizedPageSize,
            cancellationToken);

        var response = new MovieReviewDetailResponse(
            movieId,
            movie.Title,
            movie.PosterURL,
            stats.AverageRating,
            stats.TotalReviews,
            stats.RatingBreakdown,
            items,
            normalizedPage,
            normalizedPageSize,
            totalItems);

        return new GetMovieReviewDetailResult(true, null, response);
    }
}
