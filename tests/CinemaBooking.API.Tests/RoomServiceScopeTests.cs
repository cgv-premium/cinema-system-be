using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Notifications;
using CinemaBooking.Application.Rooms;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.API.Tests;

public sealed class RoomServiceScopeTests
{
    [Fact]
    public async Task CreateRoom_ForAnotherCinema_ReturnsForbiddenMessage()
    {
        var repository = new StubRoomRepository();
        var service = new RoomService(repository, new StubNotificationOutbox());

        var result = await service.CreateRoomAsync(
            2, "Room 1", 1, "ACTIVE", null, managerCinemaId: 1);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Access denied. This resource belongs to another cinema branch outside your management scope.",
            result.ErrorMessage);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task DeleteRoom_WithHistoricalShowtime_ReturnsConflictWithoutDeleting()
    {
        var repository = new StubRoomRepository
        {
            ExistingRoom = new Room { RoomID = 1, CinemaID = 1 },
            HasAnyShowtimes = true
        };
        var service = new RoomService(repository, new StubNotificationOutbox());

        var result = await service.DeleteRoomAsync(1, managerCinemaId: 1);

        Assert.False(result.Succeeded);
        Assert.Equal("Room has showtime history", result.ErrorMessage);
        Assert.Equal(0, repository.DeleteCallCount);
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

    private sealed class StubRoomRepository : IRoomRepository
    {
        public int AddCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }
        public Room? ExistingRoom { get; init; }
        public bool HasAnyShowtimes { get; init; }
        public Task<List<Room>> GetRoomsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Room>());
        public Task<List<Room>> GetRoomsByCinemaIdAsync(
            int cinemaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Room>());
        public Task<Room?> GetByIdAsync(int roomId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingRoom);
        public Task<bool> CinemaExistsAsync(int cinemaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<bool> RoomTypeExistsAsync(int roomTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<bool> NameExistsInCinemaAsync(int cinemaId, string roomName,
            int? excludingRoomId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<int> CountSeatsAsync(int roomId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
        public Task<Room> AddAsync(Room room, CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            return Task.FromResult(room);
        }
        public Task<Room?> UpdateAsync(int roomId, int cinemaId, string roomName,
            int roomTypeId, string status, string? description,
            CancellationToken cancellationToken = default) => Task.FromResult<Room?>(null);
        public Task<bool> HasActiveOrUpcomingShowtimesAsync(
            int roomId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasUpcomingShowtimesAsync(
            int roomId, DateTime now, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasAnyShowtimesAsync(
            int roomId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasAnyShowtimes);
        public Task<bool> DeleteAsync(int roomId, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            return Task.FromResult(true);
        }
    }
}
