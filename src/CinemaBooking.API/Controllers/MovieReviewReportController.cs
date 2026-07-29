using CinemaBooking.Application.Reviews;
using CinemaBooking.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.API.Controllers;

[ApiController]
[Route("api/admin/movie-reviews")]
[Authorize(Roles = Roles.Admin)]
public sealed class MovieReviewReportController : ControllerBase
{
    private readonly IMovieReviewReportService _reportService;

    public MovieReviewReportController(IMovieReviewReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string? searchTitle = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] double? minAverageRating = null,
        [FromQuery] double? maxAverageRating = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _reportService.GetDashboardAsync(
            searchTitle,
            NormalizeUtc(fromDate),
            NormalizeUtc(toDate),
            minAverageRating,
            maxAverageRating,
            sortBy,
            sortDir,
            page,
            pageSize,
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        var data = result.Data!;
        return Ok(new
        {
            items = data.Items.Select(i => new
            {
                movieId = i.MovieId,
                movieTitle = i.Title,
                posterUrl = i.PosterUrl,
                totalReviews = i.TotalReviews,
                averageRating = i.AverageRating,
                fiveStarCount = i.Count5Star,
                fourStarCount = i.Count4Star,
                threeStarCount = i.Count3Star,
                twoStarCount = i.Count2Star,
                oneStarCount = i.Count1Star,
                latestReviewDate = i.LatestReviewDate
            }),
            page = data.Page,
            pageSize = data.PageSize,
            totalItems = data.TotalItems,
            totalPages = data.TotalPages
        });
    }

    [HttpGet("{movieId:int}")]
    public async Task<IActionResult> GetDetail(
        int movieId,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? minRating = null,
        [FromQuery] int? maxRating = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _reportService.GetDetailAsync(
            movieId,
            NormalizeUtc(fromDate),
            NormalizeUtc(toDate),
            minRating,
            maxRating,
            page,
            pageSize,
            cancellationToken);

        if (!result.Succeeded)
        {
            return result.ErrorCode switch
            {
                "not_found" => NotFound(new { success = false, message = result.ErrorMessage }),
                _ => BadRequest(new { success = false, message = result.ErrorMessage })
            };
        }

        var data = result.Data!;
        var breakdown = new Dictionary<string, int>
        {
            ["5"] = data.RatingBreakdown.TryGetValue(5, out var r5) ? r5 : 0,
            ["4"] = data.RatingBreakdown.TryGetValue(4, out var r4) ? r4 : 0,
            ["3"] = data.RatingBreakdown.TryGetValue(3, out var r3) ? r3 : 0,
            ["2"] = data.RatingBreakdown.TryGetValue(2, out var r2) ? r2 : 0,
            ["1"] = data.RatingBreakdown.TryGetValue(1, out var r1) ? r1 : 0
        };

        var totalPages = data.PageSize == 0
            ? 0
            : (int)Math.Ceiling(data.TotalItems / (double)data.PageSize);

        return Ok(new
        {
            movie = new
            {
                movieId = data.MovieId,
                movieTitle = data.Title,
                posterUrl = data.PosterUrl
            },
            statistics = new
            {
                averageRating = data.AverageRating,
                totalReviews = data.TotalReviews
            },
            ratingBreakdown = breakdown,
            reviews = data.Reviews.Select(i => new
            {
                reviewId = i.ReviewId,
                userId = i.UserId,
                userName = i.UserName,
                avatar = i.UserAvatar,
                bookingId = i.BookingId,
                reviewDate = i.CreatedAt,
                rating = i.Rating,
                comment = i.Comment
            }),
            page = data.Page,
            pageSize = data.PageSize,
            totalItems = data.TotalItems,
            totalPages = totalPages
        });
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue) return null;
        var v = value.Value;
        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc)
        };
    }
}
