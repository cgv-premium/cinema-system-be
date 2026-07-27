using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Notifications;
using CinemaBooking.Application.Showtimes;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.API.Tests;

public sealed class ShowtimeServiceTests
{
    [Fact]
    public async Task CreateShowtimeAsync_RoomHasNoActiveSeats_ReturnsValidationError()
    {
        var repository = new StubShowtimeRepository { ActiveSeats = [] };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.CreateShowtimeAsync(
            1, 1, DateTime.UtcNow.AddDays(1), 100_000);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Room has no seats. Please configure seats before creating a showtime.",
            result.ErrorMessage);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Theory]
    [InlineData(0, 10, "startTime", "asc")]
    [InlineData(1, 0, "startTime", "asc")]
    [InlineData(1, 101, "startTime", "asc")]
    [InlineData(1, 10, "unknown", "asc")]
    [InlineData(1, 10, "startTime", "sideways")]
    public async Task GetShowtimesAsync_InvalidPagingOrSort_ReturnsValidationError(
        int page, int pageSize, string sortBy, string sortDir)
    {
        var service = new ShowtimeService(new StubShowtimeRepository(), new StubNotificationOutbox());

        var result = await service.GetShowtimesAsync(
            null, null, null, null, null, null, page, pageSize, sortBy, sortDir);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("id", "asc")]
    [InlineData("id", "desc")]
    [InlineData("startTime", "asc")]
    [InlineData("endTime", "desc")]
    [InlineData("basePrice", "asc")]
    [InlineData("status", "desc")]
    public async Task GetShowtimesAsync_ValidSortBy_ReturnsSuccess(
        string sortBy, string sortDir)
    {
        var service = new ShowtimeService(new StubShowtimeRepository(), new StubNotificationOutbox());

        var result = await service.GetShowtimesAsync(
            null, null, null, null, null, null, 1, 10, sortBy, sortDir);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreateShowtimeAsync_StartTimeNotNormalizedToUtc_ReturnsValidationError()
    {
        var repository = new StubShowtimeRepository
        {
            ActiveSeats = [new Seat { SeatID = 1, RoomID = 1, Status = "active" }]
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.CreateShowtimeAsync(
            1, 1, DateTime.SpecifyKind(DateTime.Now.AddDays(1), DateTimeKind.Unspecified), 100_000);

        Assert.False(result.Succeeded);
        Assert.Equal("StartTime must be normalized to UTC", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateShowtimeAsync_RoomOutsideManagerCinema_ReturnsForbiddenMessage()
    {
        var repository = new StubShowtimeRepository
        {
            ActiveSeats = [new Seat { SeatID = 1, RoomID = 1, Status = "active" }]
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.CreateShowtimeAsync(
            1, 1, DateTime.UtcNow.AddDays(1), 100_000, managerCinemaId: 2);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Access denied. This resource belongs to another cinema branch outside your management scope.",
            result.ErrorMessage);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task CreateShowtimeAsync_RoomHasActiveSeat_CreatesShowtime()
    {
        var repository = new StubShowtimeRepository
        {
            ActiveSeats = [new Seat { SeatID = 1, RoomID = 1, Status = "active" }]
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.CreateShowtimeAsync(
            1, 1, DateTime.UtcNow.AddDays(1), 100_000);

        Assert.True(result.Succeeded);
        Assert.Equal("scheduled", result.Showtime!.Status);
        Assert.Equal(1, repository.AddCallCount);
    }

    [Fact]
    public async Task CreateShowtimeAsync_StartTimeHasPassed_CreatesCompletedShowtime()
    {
        var repository = new StubShowtimeRepository
        {
            ActiveSeats = [new Seat { SeatID = 1, RoomID = 1, Status = "active" }]
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.CreateShowtimeAsync(
            1, 1, DateTime.UtcNow.AddMinutes(-1), 100_000);

        Assert.True(result.Succeeded);
        Assert.Equal("completed", result.Showtime!.Status);
    }

    [Fact]
    public async Task CreateShowtimeAsync_SameCinemaStartAndRoomType_ReturnsConflict()
    {
        var repository = new StubShowtimeRepository
        {
            ActiveSeats = [new Seat { SeatID = 1, RoomID = 1, Status = "active" }],
            HasRoomTypeStartConflict = true
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.CreateShowtimeAsync(
            1, 1, DateTime.UtcNow.AddDays(1), 100_000);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Another showtime with the same room type already starts at this time in the cinema",
            result.ErrorMessage);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task UpdateShowtimeAsync_ActiveBookingAndScheduleChange_ReturnsConflict()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var repository = new StubShowtimeRepository
        {
            HasActiveBookingOrHold = true,
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1, MovieID = 1, RoomID = 1,
                StartTime = startTime, BasePrice = 100_000, Status = "scheduled"
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.UpdateShowtimeAsync(
            1, 1, 1, startTime.AddHours(1), 100_000, null);

        Assert.False(result.Succeeded);
        Assert.Equal("Showtime has active bookings or seat holds", result.ErrorMessage);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task UpdateShowtimeAsync_ActiveBookingAndCancellation_CancelsWithoutChangingDetails()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var repository = new StubShowtimeRepository
        {
            HasActiveBookingOrHold = true,
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1, MovieID = 1, RoomID = 1,
                StartTime = startTime, BasePrice = 100_000, Status = "scheduled",
                Movie = new Movie { Title = "Test Movie" },
                Room = new Room { RoomName = "Room 1", CinemaID = 1 }
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.UpdateShowtimeAsync(
            1, 1, 1, startTime, 100_000, "CANCELLED");

        Assert.True(result.Succeeded);
        Assert.Equal("cancelled", result.Showtime!.Status);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task UpdateShowtimeAsync_StatusOnlyCancellation_DoesNotRequireActiveMovieOrRoom()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var repository = new StubShowtimeRepository
        {
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1, MovieID = 1, RoomID = 1,
                StartTime = startTime, BasePrice = 100_000, Status = "scheduled",
                Movie = new Movie { Title = "Test Movie" },
                Room = new Room
                {
                    RoomID = 1, CinemaID = 1, Status = "inactive", RoomName = "Room 1",
                    Cinema = new Cinema { CinemaID = 1, Status = "inactive" }
                }
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.UpdateShowtimeAsync(
            1, 1, 1, startTime, 100_000, "cancelled");

        Assert.True(result.Succeeded);
        Assert.Equal("cancelled", result.Showtime!.Status);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task DeleteShowtimeAsync_AnyHistoricalRelation_ReturnsConflict()
    {
        var repository = new StubShowtimeRepository
        {
            HasAnyBookingOrHold = true,
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1,
                Room = new Room { CinemaID = 1 }
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.DeleteShowtimeAsync(1);

        Assert.False(result.Succeeded);
        Assert.Equal("Showtime has booking or seat hold history", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateShowtimeAsync_ManualCompletedStatus_UsesProvidedStatus()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var repository = new StubShowtimeRepository
        {
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1, MovieID = 1, RoomID = 1,
                StartTime = startTime, BasePrice = 100_000, Status = "scheduled"
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.UpdateShowtimeAsync(
            1, 1, 1, startTime, 100_000, "COMPLETED");

        Assert.True(result.Succeeded);
        Assert.Equal("completed", result.Showtime!.Status);
    }

    [Fact]
    public async Task UpdateShowtimeAsync_CancelledWithoutStatus_RemainsCancelled()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var repository = new StubShowtimeRepository
        {
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1, MovieID = 1, RoomID = 1,
                StartTime = startTime, BasePrice = 100_000, Status = "cancelled"
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.UpdateShowtimeAsync(
            1, 1, 1, startTime, 100_000, null);

        Assert.True(result.Succeeded);
        Assert.Equal("cancelled", result.Showtime!.Status);
    }

    [Fact]
    public async Task UpdateShowtimeAsync_CancelledShowtime_ReturnsConflict()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var repository = new StubShowtimeRepository
        {
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1, MovieID = 1, RoomID = 1,
                StartTime = startTime, BasePrice = 100_000, Status = "cancelled"
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.UpdateShowtimeAsync(
            1, 1, 1, startTime, 100_000, "SCHEDULED");

        Assert.False(result.Succeeded);
        Assert.Equal("Cancelled showtime cannot be updated.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateShowtimeAsync_ActiveBookingAndNonCancelledStatus_ReturnsConflict()
    {
        var startTime = DateTime.UtcNow.AddDays(1);
        var repository = new StubShowtimeRepository
        {
            HasActiveBookingOrHold = true,
            ExistingShowtime = new Showtime
            {
                ShowtimeID = 1, MovieID = 1, RoomID = 1,
                StartTime = startTime, BasePrice = 100_000, Status = "scheduled"
            }
        };
        var service = new ShowtimeService(repository, new StubNotificationOutbox());

        var result = await service.UpdateShowtimeAsync(
            1, 1, 1, startTime, 100_000, "COMPLETED");

        Assert.False(result.Succeeded);
        Assert.Equal("Showtime has active bookings or seat holds", result.ErrorMessage);
    }

    private sealed class StubNotificationOutbox : INotificationOutbox
    {
        public Task EnqueueBookingSuccessAsync(int bookingId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueRefundCompletedAsync(int bookingId, decimal amount, DateTime completedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueWalletRefundAsync(int userId, decimal amount, DateTime occurredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueWalletPaymentAsync(int userId, decimal amount, DateTime occurredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueRevenueAnomalyAsync(int cinemaId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueDailySummaryAsync(int? cinemaId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueTopSellingMovieAsync(int movieId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueLowPerformingMovieAsync(int movieId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueMovieEndingSoonAsync(int movieId, string message, int daysRemaining, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueShowtimeSoldOutAsync(int showtimeId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueLowOccupancyShowtimeAsync(int showtimeId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueVoucherExpiringSoonAsync(int voucherId, string message, int daysRemaining, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueVoucherOutOfStockAsync(int voucherId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueNewCustomerSummaryAsync(string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueuePaymentIssueAsync(string message, string? paymentMethod = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueRoomCreatedAsync(int roomId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueRoomUpdatedAsync(int roomId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueRoomInactiveAsync(int roomId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueShowtimeStartingSoonAsync(int showtimeId, string message, int minutesRemaining, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueShowtimeCreatedAsync(int showtimeId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueShowtimeUpdatedAsync(int showtimeId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnqueueShowtimeDeletedAsync(int showtimeId, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubShowtimeRepository : IShowtimeRepository
    {
        public List<Seat> ActiveSeats { get; init; } = [];
        public Showtime? ExistingShowtime { get; init; }
        public bool HasActiveBookingOrHold { get; init; }
        public bool HasAnyBookingOrHold { get; init; }
        public bool HasRoomTypeStartConflict { get; init; }
        public int AddCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }

        public Task<Movie?> GetMovieAsync(int movieId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Movie?>(new Movie { MovieID = movieId, DurationMin = 120, Status = "now_showing" });
        public Task<Room?> GetRoomAsync(int roomId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Room?>(new Room
            {
                RoomID = roomId,
                CinemaID = 1,
                RoomTypeID = 1,
                RoomType = new RoomType { RoomTypeID = 1, TypeName = "Standard" },
                Status = "active",
                Cinema = new Cinema { CinemaID = 1, Status = "active" }
            });
        public Task<List<Seat>> GetSeatsByRoomAsync(int roomId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveSeats);
        public Task<bool> HasConflictAsync(int roomId, DateTime startTime, DateTime endTime,
            int? excludingShowtimeId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasRoomTypeStartConflictAsync(
            int cinemaId, int roomTypeId, DateTime startTime,
            int? excludingShowtimeId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasRoomTypeStartConflict);
        public Task<bool> HasValidSeatAsync(
            int roomId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveSeats.Any(seat => !seat.IsGap && seat.Status == "active"));
        public Task<Showtime> AddAsync(Showtime showtime, CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            return Task.FromResult(showtime);
        }

        public Task<(List<Showtime> Items, int TotalItems)> GetShowtimesAsync(int? movieId, int? cinemaId,
            string? movieName, string? roomName, DateOnly? date, string? status, bool onlyActiveLocations,
            int page, int pageSize,
            string sortBy, bool descending, CancellationToken cancellationToken = default) =>
            Task.FromResult((new List<Showtime>(), 0));
        public Task<bool> HasActiveBookingOrHoldAsync(int showtimeId, DateTime now,
            CancellationToken cancellationToken = default) => Task.FromResult(HasActiveBookingOrHold);
        public Task<bool> HasSuccessfulBookingAsync(int showtimeId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasAnyBookingOrHoldAsync(int showtimeId,
            CancellationToken cancellationToken = default) => Task.FromResult(HasAnyBookingOrHold);
        public Task<List<Showtime>> GetShowtimesByRangeAsync(
            DateTime startUtc, DateTime endUtc, int? cinemaId, bool onlyActiveLocations,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Showtime>());
        public Task<IReadOnlySet<int>> GetSoldOutShowtimeIdsAsync(
            IReadOnlyCollection<int> showtimeIds, DateTime now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        public Task<bool> AcquireRoomScheduleLockAsync(int roomId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> AcquireCinemaScheduleLockAsync(int cinemaId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<Showtime?> UpdateAsync(Showtime showtime, CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            return Task.FromResult<Showtime?>(showtime);
        }
        public Task<bool> DeleteAsync(int showtimeId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<Showtime?> GetShowtimeByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingShowtime?.ShowtimeID == id ? ExistingShowtime : null);
        public Task<Showtime?> GetManagedShowtimeByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingShowtime?.ShowtimeID == id ? ExistingShowtime : null);
        public Task<List<int>> GetBookedSeatIdsAsync(int showtimeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<int>());
        public Task<List<int>> GetHeldSeatIdsAsync(int showtimeId, DateTime now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<int>());
    }
}
