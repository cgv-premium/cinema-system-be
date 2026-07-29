namespace CinemaBooking.Application.Reviews;

public sealed record ReviewListItem(
    int ReviewId,
    int Rating,
    string? Comment,
    DateTime CreatedAt,
    int UserId,
    string UserFullName,
    string? UserAvatarUrl);

public sealed record MovieReviewStats(
    double? AverageRating,
    int TotalReviews,
    IReadOnlyDictionary<int, int> RatingBreakdown);

public sealed record MovieReviewPage(
    int MovieId,
    double? AverageRating,
    int TotalReviews,
    IReadOnlyDictionary<int, int> RatingBreakdown,
    IReadOnlyList<ReviewListItem> Items,
    int Page,
    int PageSize);

public sealed record AdminReviewListItem(
    int ReviewId,
    int MovieId,
    string MovieTitle,
    int UserId,
    string CustomerName,
    string? CustomerAvatar,
    int Rating,
    string? Comment,
    bool IsHidden,
    DateTime CreatedAt,
    DateTime? HiddenAt);

public sealed record AdminReviewPage(
    IReadOnlyList<AdminReviewListItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public enum AdminReviewStatusFilter
{
    All,
    Active,
    Hidden
}

public sealed record MovieReviewDashboardItem(
    int MovieId,
    string Title,
    string? PosterUrl,
    int TotalReviews,
    double? AverageRating,
    int Count5Star,
    int Count4Star,
    int Count3Star,
    int Count2Star,
    int Count1Star,
    DateTime? LatestReviewDate);

public sealed record MovieReviewDashboardPage(
    IReadOnlyList<MovieReviewDashboardItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record ReviewDetailItem(
    int ReviewId,
    int UserId,
    string UserName,
    string? UserAvatar,
    int BookingId,
    DateTime CreatedAt,
    int Rating,
    string? Comment);

public sealed record MovieReviewDetailResponse(
    int MovieId,
    string Title,
    string? PosterUrl,
    double? AverageRating,
    int TotalReviews,
    IReadOnlyDictionary<int, int> RatingBreakdown,
    IReadOnlyList<ReviewDetailItem> Reviews,
    int Page,
    int PageSize,
    int TotalItems);
