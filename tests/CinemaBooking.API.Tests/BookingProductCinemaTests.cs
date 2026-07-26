using CinemaBooking.Application.Bookings;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore.Storage;

namespace CinemaBooking.API.Tests;

public sealed class BookingProductAvailabilityTests
{
    [Fact]
    public async Task CreateBooking_InactiveProduct_ReturnsValidationError()
    {
        var repository = new StubBookingRepository();
        var service = new BookingService(repository, null!, null!, null!, null!);

        var result = await service.CreateBookingAsync(
            1, null, true, 1, [1], [new BookingFnBItemDto(10, 1)], null);

        Assert.False(result.Succeeded);
        Assert.Equal("Product 'Popcorn' is inactive.", result.ErrorMessage);
    }

    [Fact]
    public async Task ReleaseSeatHolds_AllSeatsAreOwnedAndActive_ReleasesEverySeat()
    {
        var repository = new StubBookingRepository
        {
            ActiveHoldsForUpdate =
            [
                new SeatHold { HoldID = 1, SeatID = 101, Status = "holding" },
                new SeatHold { HoldID = 2, SeatID = 102, Status = "holding" }
            ]
        };
        var service = new BookingService(repository, null!, new StubUnitOfWork(), null!, null!);

        var result = await service.ReleaseSeatHoldsAsync(7, 12, [101, 102]);

        Assert.True(result.Succeeded);
        Assert.Equal([101, 102], repository.ReleasedSeatIds);
    }

    [Fact]
    public async Task ReleaseSeatHolds_AnySeatIsNotOwnedAndActive_ReleasesNothing()
    {
        var repository = new StubBookingRepository
        {
            ActiveHoldsForUpdate =
            [
                new SeatHold { HoldID = 1, SeatID = 101, Status = "holding" }
            ]
        };
        var service = new BookingService(repository, null!, new StubUnitOfWork(), null!, null!);

        var result = await service.ReleaseSeatHoldsAsync(7, 12, [101, 102]);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "One or more seats are not actively held by the current user.",
            result.ErrorMessage);
        Assert.Empty(repository.ReleasedSeatIds);
    }

    [Fact]
    public async Task CreateBooking_WhenHoldIsNoLongerActiveInsideTransaction_DoesNotCreateBooking()
    {
        var repository = new StubBookingRepository
        {
            ActiveHolds = [new SeatHold { HoldID = 1, SeatID = 1, Status = "holding" }],
            ActiveHoldsForUpdate = []
        };
        var service = new BookingService(repository, null!, new StubUnitOfWork(), null!, null!);

        var result = await service.CreateBookingAsync(
            actorUserId: 7,
            customerId: null,
            isStaff: true,
            showtimeId: 1,
            seatIds: [1],
            fnbItems: [],
            voucherCode: null);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Some seats are not held or the holds have expired. Please select them again.",
            result.ErrorMessage);
        Assert.Equal(0, repository.AddBookingCallCount);
    }

    private sealed class StubBookingRepository : IBookingRepository
    {
        public List<SeatHold> ActiveHoldsForUpdate { get; init; } = [];
        public List<SeatHold> ActiveHolds { get; init; } =
            [new SeatHold { HoldID = 1, SeatID = 1, Status = "holding" }];
        public List<int> ReleasedSeatIds { get; } = [];
        public int AddBookingCallCount { get; private set; }

        public Task<Showtime?> GetShowtimeAsync(int showtimeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Showtime?>(new Showtime
            {
                ShowtimeID = showtimeId,
                RoomID = 1,
                Status = "scheduled",
                StartTime = DateTime.UtcNow.AddHours(2),
                BasePrice = 100_000,
                Room = new Room
                {
                    RoomID = 1,
                    CinemaID = 1,
                    Cinema = new Cinema { CinemaID = 1, Status = "active" }
                }
            });
        public Task<List<Seat>> GetSeatsByIdsAsync(List<int> seatIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Seat>
            {
                new()
                {
                    SeatID = 1, RoomID = 1, Status = "active",
                    SeatType = new SeatType { ExtraPrice = 0 }
                }
            });
        public Task<List<SeatHold>> GetMyActiveHoldsAsync(
            int userId, int showtimeId, List<int> seatIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveHolds);
        public Task<List<SeatHold>> GetMyActiveHoldsForUpdateAsync(
            int userId, int showtimeId, DateTime now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveHoldsForUpdate);
        public Task ReleaseSeatHoldsAsync(
            IEnumerable<SeatHold> seatHolds,
            CancellationToken cancellationToken = default)
        {
            ReleasedSeatIds.AddRange(seatHolds.Select(hold => hold.SeatID));
            return Task.CompletedTask;
        }
        public Task<List<Product>> GetProductsByIdsAsync(
            List<int> productIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Product>
            {
                new()
                {
                    ItemID = 10, ItemName = "Popcorn", ItemType = "snack",
                    Status = "inactive", Price = 50_000
                }
            });

        public Task<List<int>> GetUnavailableSeatIdsAsync(int showtimeId, List<int> seatIds,
            int currentUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryAddSeatHoldsAsync(IEnumerable<SeatHold> seatHolds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddBookingAsync(Booking booking, CancellationToken cancellationToken = default)
        {
            AddBookingCallCount++;
            booking.BookingID = 1;
            return Task.CompletedTask;
        }
        public Task MarkHoldsAsConfirmedAsync(IEnumerable<SeatHold> seatHolds, int bookingId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingByIdAsync(int bookingId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Booking>> GetBookingsByUserAsync(int userId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Product>> GetAvailableProductsAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Voucher?> GetVoucherByCodeAsync(string voucherCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Voucher?> GetVoucherByCodeWithLockAsync(string voucherCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task IncrementVoucherUsageAsync(int voucherId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExtendBookingHoldsAsync(int bookingId, DateTime expiresAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasActiveBookingHoldsAsync(int bookingId, DateTime now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateBookingStatusAsync(int bookingId, string status,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingByQRCodeAsync(string qrCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingWithFullDetailsForCheckInAsync(int bookingId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int?> GetStaffCinemaIdAsync(int staffId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PerformCheckInAsync(int bookingId, int staffId, string? ipAddress,
            DateTime checkedInAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(List<Booking> Bookings, int TotalCount)> GetCheckInHistoryAsync(
            int? staffId, int? cinemaId, DateTime? from, DateTime? to, int page, int pageSize,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateBookingQRCodeAsync(int bookingId, string qrCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDbContextTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Booking?> GetBookingByCodeAsync(string bookingCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateBookingFnBPickupAsync(string bookingCode, int staffId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteSeatHoldsByBookingIdAsync(int bookingId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(List<Booking> Bookings, int TotalCount)> GetFnBPickupHistoryAsync(
            int? staffId, int? cinemaId, DateTime? from, DateTime? to, int page, int pageSize,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(int TotalSeats, int BookedSeats)> GetShowtimeOccupancyAsync(int showtimeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default) => operation();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
