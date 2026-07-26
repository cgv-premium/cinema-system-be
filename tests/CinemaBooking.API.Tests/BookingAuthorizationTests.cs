using System.Security.Claims;
using CinemaBooking.API.Contracts.Bookings;
using CinemaBooking.API.Controllers;
using CinemaBooking.Application.Bookings;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Contracts.Payment;
using CinemaBooking.Application.Payments;
using CinemaBooking.Application.Payments.PayOS;
using CinemaBooking.Application.Reviews;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.API.Tests;

public sealed class BookingAuthorizationTests
{
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Manager)]
    public async Task CreateBooking_AdminOrManager_ReturnsForbidden(string role)
    {
        var service = new StubBookingService();
        var controller = CreateController(service, role);

        var result = await controller.CreateBooking(CreateRequest(), CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(0, service.CreateBookingCallCount);
        Assert.Equal(
            "You do not have permission to create a booking.",
            forbidden.Value?.GetType().GetProperty("message")?.GetValue(forbidden.Value));
    }

    [Theory]
    [InlineData(Roles.Customer)]
    [InlineData(Roles.Staff)]
    public async Task CreateBooking_CustomerOrStaff_ReachesBookingService(string role)
    {
        var service = new StubBookingService();
        var controller = CreateController(service, role);

        var result = await controller.CreateBooking(CreateRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(1, service.CreateBookingCallCount);
    }

    private static BookingController CreateController(
        StubBookingService service,
        string role)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("userId", "1"),
            new Claim(ClaimTypes.Role, role)
        ], "Test");

        return new BookingController(service, new StubBookingRepository(), new StubPaymentService(), new StubMovieReviewRepository(), new StubLoyaltyRepository(), new StubReviewRewardSettingsRepository())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static CreateBookingRequest CreateRequest()
    {
        return new CreateBookingRequest
        {
            ShowtimeId = 1,
            SeatIds = [1]
        };
    }

    private sealed class StubBookingService : IBookingService
    {
        public int CreateBookingCallCount { get; private set; }

        public Task<(bool Succeeded, string? ErrorMessage, List<int>? HoldIds, DateTime? ExpiresAt, SeatValidationErrors? SeatErrors)> HoldSeatsAsync(
            int userId,
            int showtimeId,
            List<int> seatIds,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<(bool Succeeded, string? ErrorMessage)> ReleaseSeatHoldsAsync(
            int userId,
            int showtimeId,
            List<int> seatIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(bool Succeeded, string? ErrorMessage, Booking? Booking, SeatValidationErrors? SeatErrors)> CreateBookingAsync(
            int actorUserId,
            int? customerId,
            bool isStaff,
            int? showtimeId,
            List<int> seatIds,
            List<BookingFnBItemDto> fnbItems,
            string? voucherCode,
            int? staffCinemaId = null,
            CancellationToken cancellationToken = default)
        {
            CreateBookingCallCount++;
            return Task.FromResult<(bool, string?, Booking?, SeatValidationErrors?)>(
                (false, "Booking validation stopped the request.", null, null));
        }

        public Task<(bool Succeeded, string? ErrorMessage, PricingCalculationResult? Result)> CalculatePricingAsync(
            int? userId,
            int? showtimeId,
            List<int> seatIds,
            List<BookingFnBItemDto> fnbItems,
            string? voucherCode,
            int? staffCinemaId = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Booking?> GetBookingByIdAsync(
            int bookingId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<Booking>> GetMyBookingsAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<(bool Succeeded, string? ErrorMessage, LookupBookingFnbResult? Result)> LookupBookingFnbAsync(
            string bookingCode,
            int staffId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubMovieReviewRepository : IMovieReviewRepository
    {
        public Task<MovieReview> AddAsync(MovieReview review, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MovieReview?> GetByIdAsync(int reviewId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(int reviewId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> BookingHasReviewAsync(int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UserHasReviewedMovieAsync(int userId, int movieId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UserHasAnyReviewAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> MovieExistsAsync(int movieId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MovieReview?> GetForUpdateAsync(int reviewId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(MovieReview review, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MovieReviewStats> GetVisibleStatsForMovieAsync(int movieId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, MovieReviewStats>> GetVisibleStatsForMoviesAsync(IReadOnlyCollection<int> movieIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ReviewListItem>> GetVisibleReviewsForMovieAsync(int movieId, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(int? ReviewId, bool HasReview)> GetBookingReviewLookupAsync(int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, int>> GetReviewIdsByBookingIdsAsync(IReadOnlyCollection<int> bookingIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<AdminReviewListItem> Items, int Total)> SearchAdminReviewsAsync(string? keyword, int? movieId, AdminReviewStatusFilter status, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubLoyaltyRepository : ILoyaltyRepository
    {
        public Task<int> GetUserTotalPointsAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<decimal> GetUserTotalSpentAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoyaltyTier?> GetUserTierAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LoyaltyTier>> GetAllTiersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoyaltyTier?> GetTierByIdAsync(int tierId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TierNameExistsAsync(string tierName, int? excludingTierId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> MinPointsExistsAsync(int minPoints, int? excludingTierId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoyaltyTier> AddTierAsync(LoyaltyTier tier, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoyaltyTier?> UpdateTierAsync(int tierId, string tierName, int minPoints, decimal discountRate, int maxRefundPerMonth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAssignedUsersAsync(int tierId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteTierAsync(int tierId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddLoyaltyPointAsync(LoyaltyPoints loyaltyPoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LoyaltyPoints>> GetPointHistoryAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateUserTierAsync(int userId, int tierID, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateUserTotalPointsAsync(int userId, int totalPoints, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasPointsForBookingAsync(int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<int>> GetBookingIdsWithEarnedPointsAsync(IReadOnlyCollection<int> bookingIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LoyaltyTier>> GetTiersByIdsAsync(List<int> tierIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubReviewRewardSettingsRepository : IReviewRewardSettingsRepository
    {
        public Task<ReviewRewardSettings?> GetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(ReviewRewardSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubBookingRepository : IBookingRepository
    {
        public Task<int?> GetStaffCinemaIdAsync(int staffId, CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(1);

        public Task<Showtime?> GetShowtimeAsync(int showtimeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Seat>> GetSeatsByIdsAsync(List<int> seatIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<int>> GetUnavailableSeatIdsAsync(int showtimeId, List<int> seatIds, int currentUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryAddSeatHoldsAsync(IEnumerable<SeatHold> seatHolds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<SeatHold>> GetMyActiveHoldsAsync(int userId, int showtimeId, List<int> seatIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<SeatHold>> GetMyActiveHoldsForUpdateAsync(int userId, int showtimeId, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseSeatHoldsAsync(IEnumerable<SeatHold> seatHolds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddBookingAsync(Booking booking, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkHoldsAsConfirmedAsync(IEnumerable<SeatHold> seatHolds, int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingByIdAsync(int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingByCodeAsync(string bookingCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingWithFullDetailsForCheckInAsync(int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(List<Booking> Bookings, int TotalCount)> GetCheckInHistoryAsync(int? staffId, int? cinemaId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateBookingQRCodeAsync(int bookingId, string qrCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Booking>> GetBookingsByUserAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Product>> GetProductsByIdsAsync(List<int> productIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Product>> GetAvailableProductsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Voucher?> GetVoucherByCodeAsync(string voucherCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Voucher?> GetVoucherByCodeWithLockAsync(string voucherCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task IncrementVoucherUsageAsync(int voucherId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExtendBookingHoldsAsync(int bookingId, DateTime expiresAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasActiveBookingHoldsAsync(int bookingId, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateBookingStatusAsync(int bookingId, string status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateBookingFnBPickupAsync(string bookingCode, int staffId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteSeatHoldsByBookingIdAsync(int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(List<Booking> Bookings, int TotalCount)> GetFnBPickupHistoryAsync(int? staffId, int? cinemaId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(int TotalSeats, int BookedSeats)> GetShowtimeOccupancyAsync(int showtimeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubPaymentService : IPaymentService
    {
        public Task<PaymentOperationResult> InitiatePaymentAsync(
            InitiatePaymentRequest request,
            int actorUserId,
            bool isStaff,
            string? frontendOrigin = null,
            string? backendOrigin = null,
            string ipAddress = "127.0.0.1",
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PaymentOperationResult> ConfirmCashPaymentAsync(
            ConfirmCashPaymentRequest request,
            int staffUserId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PayOSWebhookResult> ProcessPayOSWebhookAsync(
            PayOSWebhook webhook,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PaymentOperationResult> SyncPayOSPaymentAsync(
            int bookingId,
            long orderCode,
            int actorUserId,
            bool isStaff,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> ReconcilePendingPayOSPaymentsAsync(
            int batchSize = 50,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PayOSRedirectResult> HandlePayOSRedirectAsync(
            long orderCode,
            bool isCancel,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PaymentOperationResult> GetPaymentByIdAsync(
            int paymentId,
            int actorUserId,
            bool isStaff,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PaymentOperationResult> GetPaymentByBookingIdAsync(
            int bookingId,
            int actorUserId,
            bool isStaff,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
