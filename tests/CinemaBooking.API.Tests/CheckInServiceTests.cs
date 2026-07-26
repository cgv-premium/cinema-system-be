using CinemaBooking.Application.CheckIns;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Membership;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;
using System;

namespace CinemaBooking.API.Tests;

public sealed class CheckInServiceTests
{
    #region Lookup Tests

    [Fact]
    public async Task LookupAsync_TicketNotFound_ReturnsFailure()
    {
        var ticketRepo = new StubTicketRepository { TicketToReturn = null };
        var bookingRepo = new StubBookingRepository();
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.LookupAsync("INVALID_QR", staffId: 1);

        Assert.False(result.Succeeded);
        Assert.Equal("Ticket not found.", result.ErrorMessage);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task LookupAsync_MissingBooking_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat = null;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository();
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.LookupAsync("QR123", staffId: 1);

        Assert.False(result.Succeeded);
        Assert.Equal("Ticket data is incomplete (missing booking).", result.ErrorMessage);
    }

    [Fact]
    public async Task LookupAsync_StaffWithNoCinemaAssignment_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = null };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.LookupAsync("QR123", staffId: 1);

        Assert.False(result.Succeeded);
        Assert.Equal("Staff cinema assignment not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task LookupAsync_IncompleteBookingData_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat!.Booking!.Showtime = null;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.LookupAsync("QR123", staffId: 1);

        Assert.False(result.Succeeded);
        Assert.Equal("Booking data is incomplete (missing showtime/room/cinema).", result.ErrorMessage);
    }

    [Fact]
    public async Task LookupAsync_StaffFromDifferentCinema_ReturnsSuccessWithWarning()
    {
        var ticket = CreateValidTicket();
        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 999 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.LookupAsync("QR123", staffId: 1);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsFromOtherCinema);
        Assert.NotNull(result.Data.WarningMessage);
        Assert.Contains("another cinema", result.Data.WarningMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LookupAsync_ValidRequest_ReturnsSuccessWithData()
    {
        var ticket = CreateValidTicket();
        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.LookupAsync("QR123", staffId: 1);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Data);
        Assert.Equal(100, result.Data.BookingId);
        Assert.Equal("BK12345", result.Data.BookingCode);
        Assert.Equal("John Doe", result.Data.CustomerName);
        Assert.Equal("Avengers", result.Data.Movie.Title);
        Assert.Equal("Cinema A", result.Data.Cinema.Name);
        Assert.Equal("Room 1", result.Data.Room.Name);
        Assert.Single(result.Data.Seats);
        Assert.Equal("A", result.Data.Seats[0].Row);
        Assert.Equal(1, result.Data.Seats[0].Column);
        Assert.False(result.Data.IsFromOtherCinema);
        Assert.Null(result.Data.WarningMessage);
    }

    [Fact]
    public async Task LookupAsync_CheckedInTicket_ShowsCheckInStatus()
    {
        var ticket = CreateValidTicket();
        var checkedInTime = DateTime.UtcNow.AddMinutes(-10);
        ticket.Status = TicketStatus.Used;
        ticket.CheckedInAt = checkedInTime;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.LookupAsync("QR123", staffId: 1);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.Seats[0].IsCheckedIn);
        Assert.Equal(checkedInTime, result.Data.Seats[0].CheckedInAt);
    }

    #endregion

    #region History Tests

    [Fact]
    public async Task GetHistoryAsync_ReturnsCorrectData()
    {
        var booking = CreateValidBooking();
        var bookingRepo = new StubBookingRepository
        {
            CheckInHistory = new List<Booking> { booking },
            CheckInHistoryTotalCount = 1
        };
        var ticketRepo = new StubTicketRepository();
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.GetHistoryAsync(
            staffId: 1,
            cinemaId: null,
            from: null,
            to: null,
            page: 1,
            pageSize: 10);

        Assert.Single(result.Records);
        Assert.Equal(1, result.TotalCount);
        var record = result.Records[0];
        Assert.Equal(100, record.BookingId);
        Assert.Equal("BK12345", record.BookingCode);
        Assert.Equal("John Doe", record.CustomerName);
        Assert.Equal("Avengers", record.MovieTitle);
        Assert.Equal("Cinema A", record.CinemaName);
        Assert.Equal("Room 1", record.RoomName);
    }

    [Fact]
    public async Task GetHistoryAsync_EmptyResults_ReturnsEmptyList()
    {
        var bookingRepo = new StubBookingRepository
        {
            CheckInHistory = new List<Booking>(),
            CheckInHistoryTotalCount = 0
        };
        var ticketRepo = new StubTicketRepository();
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.GetHistoryAsync(
            staffId: 999,
            cinemaId: null,
            from: null,
            to: null,
            page: 1,
            pageSize: 10);

        Assert.Empty(result.Records);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetHistoryAsync_WithAllFilters_PassesToRepository()
    {
        var bookingRepo = new StubBookingRepository
        {
            CheckInHistory = new List<Booking>(),
            CheckInHistoryTotalCount = 0
        };
        var ticketRepo = new StubTicketRepository();
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var fromDate = DateTime.UtcNow.AddDays(-7);
        var toDate = DateTime.UtcNow;

        var result = await service.GetHistoryAsync(
            staffId: 42,
            cinemaId: 5,
            from: fromDate,
            to: toDate,
            page: 2,
            pageSize: 20);

        Assert.NotNull(result);
    }

    #endregion

    #region Helper Methods

    private static Booking CreateValidBooking()
    {
        var cinema = new Cinema
        {
            CinemaID = 1,
            CinemaName = "Cinema A",
            Address = "123 Main St"
        };

        var roomType = new RoomType { TypeName = "Standard" };
        var room = new Room
        {
            RoomID = 1,
            RoomName = "Room 1",
            CinemaID = 1,
            Cinema = cinema,
            RoomType = roomType
        };

        var movie = new Movie
        {
            MovieID = 1,
            Title = "Avengers",
            AgeRating = "PG-13",
            DurationMin = 120,
            PosterURL = "poster.jpg"
        };

        var showtime = new Showtime
        {
            ShowtimeID = 1,
            MovieID = 1,
            Movie = movie,
            RoomID = 1,
            Room = room,
            StartTime = DateTime.UtcNow.AddMinutes(20),
            EndTime = DateTime.UtcNow.AddMinutes(140)
        };

        var user = new User
        {
            UserID = 1,
            FullName = "John Doe"
        };

        var checkedInByUser = new User
        {
            UserID = 2,
            FullName = "Staff Member"
        };

        var booking = new Booking
        {
            BookingID = 100,
            BookingCode = "BK12345",
            UserID = 1,
            User = user,
            ShowtimeID = 1,
            Showtime = showtime,
            Status = BookingStatus.Paid,
            BookingSeats = new List<BookingSeat> { new BookingSeat() },
            FinalAmount = 50000
        };

        return booking;
    }

    private static Ticket CreateValidTicket()
    {
        var cinema = new Cinema
        {
            CinemaID = 1,
            CinemaName = "Cinema A",
            Address = "123 Main St"
        };

        var roomType = new RoomType { TypeName = "Standard" };
        var room = new Room
        {
            RoomID = 1,
            RoomName = "Room 1",
            CinemaID = 1,
            Cinema = cinema,
            RoomType = roomType
        };

        var movie = new Movie
        {
            MovieID = 1,
            Title = "Avengers",
            AgeRating = "PG-13",
            DurationMin = 120,
            PosterURL = "poster.jpg"
        };

        var showtime = new Showtime
        {
            ShowtimeID = 1,
            MovieID = 1,
            Movie = movie,
            RoomID = 1,
            Room = room,
            StartTime = DateTime.UtcNow.AddMinutes(20),
            EndTime = DateTime.UtcNow.AddMinutes(140)
        };

        var user = new User
        {
            UserID = 1,
            FullName = "John Doe"
        };

        var payment = new Payment
        {
            PaymentID = 1,
            Status = PaymentStatus.Completed
        };

        var booking = new Booking
        {
            BookingID = 100,
            BookingCode = "BK12345",
            UserID = 1,
            User = user,
            ShowtimeID = 1,
            Showtime = showtime,
            Status = BookingStatus.Paid,
            Payment = payment,
            BookingSeats = new List<BookingSeat>(),
            BookingFnBs = new List<BookingFnB>(),
            Refunds = new List<Refund>()
        };

        var seatType = new SeatType { TypeName = "Standard" };
        var seat = new Seat
        {
            SeatID = 1,
            SeatRow = "A",
            SeatCol = 1,
            SeatType = seatType
        };

        var ticket = new Ticket
        {
            TicketID = 1,
            QRCode = "QR123",
            Status = TicketStatus.Valid,
            BookingSeatID = 1
        };

        var bookingSeat = new BookingSeat
        {
            BookingSeatID = 1,
            BookingID = 100,
            Booking = booking,
            SeatID = 1,
            Seat = seat,
            TicketPrice = 50000,
            Ticket = ticket
        };

        ticket.BookingSeat = bookingSeat;
        booking.BookingSeats.Add(bookingSeat);

        return ticket;
    }

    #endregion

    #region CheckIn Tests

    [Fact]
    public async Task CheckInAsync_TicketNotFound_ReturnsFailure()
    {
        var ticketRepo = new StubTicketRepository { TicketToReturn = null };
        var bookingRepo = new StubBookingRepository();
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("INVALID_QR", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Ticket not found.", result.ErrorMessage);
        Assert.False(ticketRepo.CheckInPerformed);
    }

    [Fact]
    public async Task CheckInAsync_MissingBooking_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat = null;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository();
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Ticket data is incomplete (missing booking).", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_StaffWithNoCinemaAssignment_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = null };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Staff cinema assignment not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_StaffFromDifferentCinema_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 999 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("You cannot check in tickets from another cinema.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_PaymentNotCompleted_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat!.Booking!.Payment!.Status = "pending";

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Booking has not been paid.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_BookingCancelled_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat!.Booking!.Status = BookingStatus.Cancelled;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Booking has been cancelled.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_BookingHasCompletedRefund_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        var refund = new Refund { RefundID = 1, Status = "completed" };
        ticket.BookingSeat!.Booking!.Refunds.Add(refund);

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Ticket has been refunded.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_TicketAlreadyUsed_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.Status = TicketStatus.Used;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("This ticket has already been checked in.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_TicketCancelled_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.Status = TicketStatus.Cancelled;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("This ticket has been cancelled.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_TicketRefunded_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.Status = TicketStatus.Refunded;

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("This ticket has been refunded.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_TooEarly_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat!.Booking!.Showtime!.StartTime = DateTime.UtcNow.AddMinutes(31);
        ticket.BookingSeat.Booking.Showtime.EndTime = DateTime.UtcNow.AddMinutes(151);

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Check-in time has expired.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_TooLate_ReturnsFailure()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat!.Booking!.Showtime!.StartTime = DateTime.UtcNow.AddMinutes(-16);
        ticket.BookingSeat.Booking.Showtime.EndTime = DateTime.UtcNow.AddMinutes(104);

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Equal("Check-in time has expired.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckInAsync_AtEarliestAllowedTime_Succeeds()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat!.Booking!.Showtime!.StartTime = DateTime.UtcNow.AddMinutes(30);
        ticket.BookingSeat.Booking.Showtime.EndTime = DateTime.UtcNow.AddMinutes(150);

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.True(result.Succeeded);
        Assert.True(ticketRepo.CheckInPerformed);
    }

    [Fact]
    public async Task CheckInAsync_AtLatestAllowedTime_Succeeds()
    {
        var ticket = CreateValidTicket();
        ticket.BookingSeat!.Booking!.Showtime!.StartTime = DateTime.UtcNow.AddMinutes(-14);
        ticket.BookingSeat.Booking.Showtime.EndTime = DateTime.UtcNow.AddMinutes(106);

        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 1, ipAddress: "127.0.0.1");

        Assert.True(result.Succeeded);
        Assert.True(ticketRepo.CheckInPerformed);
    }

    [Fact]
    public async Task CheckInAsync_ValidRequest_PerformsCheckInWithCorrectData()
    {
        var ticket = CreateValidTicket();
        var ticketRepo = new StubTicketRepository { TicketToReturn = ticket };
        var bookingRepo = new StubBookingRepository { StaffCinemaId = 1 };
        var service = new CheckInService(ticketRepo, bookingRepo, new StubMembershipService());

        var result = await service.CheckInAsync("QR123", staffId: 42, ipAddress: "192.168.1.100");

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("BK12345", result.BookingCode);
        Assert.NotNull(result.CheckedInAt);
        Assert.True(ticketRepo.CheckInPerformed);
        Assert.Equal(1, ticketRepo.CheckedInTicketId);
        Assert.Equal(100, ticketRepo.CheckedInBookingId);
        Assert.Equal(42, ticketRepo.CheckedInStaffId);
        Assert.Equal("192.168.1.100", ticketRepo.CheckedInIpAddress);
    }

    #endregion

    #region Stub Implementations

    private sealed class StubTicketRepository : ITicketRepository
    {
        public Ticket? TicketToReturn { get; set; }
        public bool CheckInPerformed { get; set; }
        public int? CheckedInTicketId { get; set; }
        public int? CheckedInBookingId { get; set; }
        public int? CheckedInStaffId { get; set; }
        public string? CheckedInIpAddress { get; set; }

        public Task<Ticket?> GetTicketWithFullDetailsForCheckInAsync(string qrCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TicketToReturn);
        }

        public Task<(bool Success, bool BookingBecameUsed)> PerformTicketCheckInAsync(
            int ticketId,
            int bookingId,
            int staffId,
            string? ipAddress,
            DateTime checkedInAt,
            CancellationToken cancellationToken = default)
        {
            CheckInPerformed = true;
            CheckedInTicketId = ticketId;
            CheckedInBookingId = bookingId;
            CheckedInStaffId = staffId;
            CheckedInIpAddress = ipAddress;
            return Task.FromResult((true, false));
        }

        public Task<Ticket> CreateTicketAsync(Ticket ticket, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<HashSet<int>> GetTicketedBookingSeatIdsAsync(int bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<Ticket>> GetTicketsByBookingIdAsync(int bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Ticket?> GetTicketByIdAsync(int ticketId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Ticket?> GetTicketByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<Ticket>> GetTicketsByUserIdAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task UpdateTicketsStatusByBookingAsync(int bookingId, string status, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> AreAllTicketsUsedInBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubMembershipService : IMembershipService
    {
        public Task<MembershipInfo> GetMyMembershipAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LoyaltyTier>> GetTiersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CreateTierResult> CreateTierAsync(string tierName, int minPoints, decimal discountRate, int maxRefundPerMonth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LoyaltyPointHistory>> GetPointHistoryAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddPointsAfterPaymentSuccessAsync(int userId, int bookingId, decimal finalAmount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CheckAndUpgradeTierAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubBookingRepository : IBookingRepository
    {
        public int? StaffCinemaId { get; set; } = 1;
        public List<Booking> CheckInHistory { get; set; } = new();
        public int CheckInHistoryTotalCount { get; set; } = 0;

        public Task<int?> GetStaffCinemaIdAsync(int staffId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StaffCinemaId);
        }

        public Task<(List<Booking> Bookings, int TotalCount)> GetCheckInHistoryAsync(
            int? staffId,
            int? cinemaId,
            DateTime? from,
            DateTime? to,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((CheckInHistory, CheckInHistoryTotalCount));
        }

        public Task<Showtime?> GetShowtimeAsync(int showtimeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<Seat>> GetSeatsByIdsAsync(List<int> seatIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<int>> GetUnavailableSeatIdsAsync(int showtimeId, List<int> seatIds, int currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> TryAddSeatHoldsAsync(IEnumerable<SeatHold> seatHolds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<SeatHold>> GetMyActiveHoldsAsync(int userId, int showtimeId, List<int> seatIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<SeatHold>> GetMyActiveHoldsForUpdateAsync(int userId, int showtimeId, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ReleaseSeatHoldsAsync(IEnumerable<SeatHold> seatHolds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task AddBookingAsync(Booking booking, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task MarkHoldsAsConfirmedAsync(IEnumerable<SeatHold> seatHolds, int bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Booking?> GetBookingByIdAsync(int bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Booking?> GetBookingByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Booking?> GetBookingByCodeAsync(string bookingCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Booking?> GetBookingWithFullDetailsForCheckInAsync(int bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task UpdateBookingQRCodeAsync(int bookingId, string qrCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<Booking>> GetBookingsByUserAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<Product>> GetProductsByIdsAsync(List<int> productIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<Product>> GetAvailableProductsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Voucher?> GetVoucherByCodeAsync(string voucherCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Voucher?> GetVoucherByCodeWithLockAsync(string voucherCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task IncrementVoucherUsageAsync(int voucherId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ExtendBookingHoldsAsync(int bookingId, DateTime expiresAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> HasActiveBookingHoldsAsync(int bookingId, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task UpdateBookingStatusAsync(int bookingId, string status, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> UpdateBookingFnBPickupAsync(string bookingCode, int staffId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteSeatHoldsByBookingIdAsync(int bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<(List<Booking> Bookings, int TotalCount)> GetFnBPickupHistoryAsync(
            int? staffId, int? cinemaId, DateTime? from, DateTime? to, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(int TotalSeats, int BookedSeats)> GetShowtimeOccupancyAsync(int showtimeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    #endregion
}
