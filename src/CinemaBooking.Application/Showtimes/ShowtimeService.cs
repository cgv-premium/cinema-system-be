using CinemaBooking.Application.Common.Enums;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Common.Security;
using CinemaBooking.Application.Notifications;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Showtimes;

public sealed class ShowtimeService : IShowtimeService
{
    private const string InvalidStatusMessage =
        "Invalid showtime status. (scheduled, completed, cancelled)";
    private const string InvalidManualStatus = "__invalid_status__";
    private const string StartTimeMustBeUtcMessage =
        "StartTime must be normalized to UTC";
    private readonly IShowtimeRepository _showtimeRepository;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly INotificationOutbox _notificationOutbox;

    public ShowtimeService(
        IShowtimeRepository showtimeRepository,
        INotificationOutbox notificationOutbox,
        IUnitOfWork? unitOfWork = null)
    {
        _showtimeRepository = showtimeRepository;
        _notificationOutbox = notificationOutbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<(bool Succeeded, string? ErrorMessage, ShowtimePageResult? Page)> GetShowtimesAsync(
        int? movieId, int? cinemaId, string? movieName, string? roomName,
        DateOnly? date, string? status,
        int page, int pageSize, string? sortBy, string? sortDir,
        int? managerCinemaId = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return (false, "Page must be greater than or equal to 1", null);
        if (pageSize is < 1 or > 100) return (false, "PageSize must be between 1 and 100", null);

        var normalizedSort = sortBy?.Trim().ToLowerInvariant();
        if (normalizedSort is not ("starttime" or "endtime" or "baseprice" or "status" or "id"))
            return (false, "SortBy must be startTime, endTime, basePrice, status, or id", null);
        if (!string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase))
            return (false, "SortDir must be asc or desc", null);

        if (managerCinemaId.HasValue)
        {
            if (cinemaId.HasValue && cinemaId != managerCinemaId)
                return (false, CinemaScopeMessages.AccessDenied, null);
            cinemaId = managerCinemaId;
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusResult = EnumValueMapper.Validate(
                status, "Status", DatabaseEnumMappings.ShowtimeStatuses);
            if (!statusResult.Succeeded) return (false, InvalidStatusMessage, null);
            normalizedStatus = statusResult.DatabaseValue;
        }
        else if (!managerCinemaId.HasValue)
        {
            normalizedStatus = "scheduled";
        }

        var result = await _showtimeRepository.GetShowtimesAsync(
            movieId, cinemaId, movieName?.Trim(), roomName?.Trim(), date, normalizedStatus,
            onlyActiveLocations: !managerCinemaId.HasValue,
            page, pageSize, normalizedSort, string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
        var soldOutIds = await _showtimeRepository.GetSoldOutShowtimeIdsAsync(
            result.Items.Select(item => item.ShowtimeID).ToArray(), DateTime.UtcNow, cancellationToken);
        return (true, null,
            new ShowtimePageResult(result.Items, soldOutIds, page, pageSize, result.TotalItems));
    }

    public Task<(bool Succeeded, string? ErrorMessage, Showtime? Showtime)> CreateShowtimeAsync(
        int movieId, int roomId, DateTime startTime, decimal basePrice,
        int? managerCinemaId = null,
        CancellationToken cancellationToken = default) =>
        SaveAsync(null, movieId, roomId, startTime, basePrice, null, managerCinemaId, cancellationToken);

    public async Task<(bool Succeeded, string? ErrorMessage, Showtime? Showtime)> UpdateShowtimeAsync(
        int id, int movieId, int roomId, DateTime startTime, decimal basePrice, string? status,
        int? managerCinemaId = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await _showtimeRepository.GetManagedShowtimeByIdAsync(id, cancellationToken);
        if (existing is null) return (false, "Showtime not found", null);
        if (managerCinemaId.HasValue && existing.Room.CinemaID != managerCinemaId)
            return (false, CinemaScopeMessages.AccessDenied, null);

        var manualStatus = NormalizeManualStatus(status);
        if (manualStatus == InvalidManualStatus) return (false, InvalidStatusMessage, null);
        if (startTime.Kind != DateTimeKind.Utc)
            return (false, StartTimeMustBeUtcMessage, null);

        var isStatusChanging = manualStatus is not null && manualStatus != existing.Status;
        if (isStatusChanging)
        {
            if (await _showtimeRepository.HasAnyBookingOrHoldAsync(id, cancellationToken))
                return (false, "Showtime has booking or seat hold history", null);
        }

        if (manualStatus == "cancelled")
        {
            existing.Status = "cancelled";
            var cancelled = await _showtimeRepository.UpdateAsync(existing, cancellationToken);
            if (cancelled is null) return (false, "Showtime not found", null);
            await _notificationOutbox.EnqueueShowtimeUpdatedAsync(id,
                $"Showtime for {existing.Movie?.Title} in {existing.Room?.RoomName} at {existing.StartTime:HH:mm dd/MM/yyyy} has been deleted.",
                cancellationToken);
            return (true, null, cancelled);
        }

        var hasProtectedChanges = existing.MovieID != movieId
            || existing.RoomID != roomId
            || existing.StartTime != startTime
            || existing.BasePrice != basePrice;
        var hasActiveBookingOrHold = await _showtimeRepository.HasActiveBookingOrHoldAsync(
            id, DateTime.UtcNow, cancellationToken);
        if (hasActiveBookingOrHold && hasProtectedChanges)
            return (false, "Showtime has active bookings or seat holds", null);

        return await SaveAsync(
            existing, movieId, roomId, startTime, basePrice, status, managerCinemaId, cancellationToken);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteShowtimeAsync(
        int id, int? managerCinemaId = null, CancellationToken cancellationToken = default)
    {
        var showtime = await _showtimeRepository.GetManagedShowtimeByIdAsync(id, cancellationToken);
        if (showtime is null) return (false, "Showtime not found");
        if (managerCinemaId.HasValue && showtime.Room.CinemaID != managerCinemaId)
            return (false, CinemaScopeMessages.AccessDenied);
        if (await _showtimeRepository.HasAnyBookingOrHoldAsync(id, cancellationToken))
            return (false, "Showtime has booking or seat hold history");
        if (!await _showtimeRepository.DeleteAsync(id, cancellationToken))
            return (false, "Showtime not found");
        await _notificationOutbox.EnqueueShowtimeDeletedAsync(id,
            $"Showtime for {showtime.Movie.Title} in {showtime.Room.RoomName} at {showtime.StartTime:HH:mm dd/MM/yyyy} has been deleted.",
            cancellationToken);
        return (true, null);
    }

    public async Task<bool> IsSoldOutAsync(
        Showtime showtime, CancellationToken cancellationToken = default) =>
        (await _showtimeRepository.GetSoldOutShowtimeIdsAsync(
            [showtime.ShowtimeID], DateTime.UtcNow, cancellationToken)).Contains(showtime.ShowtimeID);

    public async Task<(bool Succeeded, string? ErrorMessage, IReadOnlyList<Showtime> Items, IReadOnlySet<int> SoldOutShowtimeIds)>
        GetShowtimesByRangeAsync(
            DateOnly startDate,
            DateOnly endDate,
            int? cinemaId = null,
            int? managerCinemaId = null,
            CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return (false, "endDate must be greater than or equal to startDate", [], new HashSet<int>());

        if (managerCinemaId.HasValue)
        {
            if (cinemaId.HasValue && cinemaId != managerCinemaId)
                return (false, CinemaScopeMessages.AccessDenied, [], new HashSet<int>());
            cinemaId = managerCinemaId;
        }

        var (fromUtc, _) = CinemaBooking.Shared.Time.VietnamTime.GetUtcDayRange(startDate);
        var (_, toUtc) = CinemaBooking.Shared.Time.VietnamTime.GetUtcDayRange(endDate);
        var items = await _showtimeRepository.GetShowtimesByRangeAsync(
            fromUtc,
            toUtc,
            cinemaId,
            onlyActiveLocations: !managerCinemaId.HasValue,
            cancellationToken);
        var soldOutIds = await _showtimeRepository.GetSoldOutShowtimeIdsAsync(
            items.Select(item => item.ShowtimeID).ToArray(), DateTime.UtcNow, cancellationToken);
        return (true, null, items, soldOutIds);
    }

    private async Task<(bool Succeeded, string? ErrorMessage, Showtime? Showtime)> SaveAsync(
        Showtime? existing, int movieId, int roomId, DateTime startTime,
        decimal basePrice, string? status, int? managerCinemaId, CancellationToken cancellationToken)
    {
        var manualStatus = NormalizeManualStatus(status);
        if (manualStatus == InvalidManualStatus) return (false, InvalidStatusMessage, null);
        if (startTime.Kind != DateTimeKind.Utc)
            return (false, StartTimeMustBeUtcMessage, null);
        if (startTime <= DateTime.UtcNow)
            return (false, "StartTime must be in the future.", null);
        if (startTime <= DateTime.UtcNow.AddMinutes(30))
            return (false, "StartTime must be at least 30 minutes from now.", null);
        if (basePrice < 0) return (false, "Base price must be greater than or equal to 0", null);

        var movie = await _showtimeRepository.GetMovieAsync(movieId, cancellationToken);
        if (movie is null) return (false, "Movie not found", null);
        if (movie.Status is not ("now_showing" or "coming_soon"))
            return (false, "The movie must be 'now_showing' or 'coming_soon' to update or create showtimes.", null);

        var room = await _showtimeRepository.GetRoomAsync(roomId, cancellationToken);
        if (room is null) return (false, "Room not found", null);
        if (managerCinemaId.HasValue && room.CinemaID != managerCinemaId)
            return (false, CinemaScopeMessages.AccessDenied, null);
        if (room.Status != "active" || room.Cinema.Status != "active")
            return (false, "Room and cinema must be active", null);
        if (existing is null && !await _showtimeRepository.HasValidSeatAsync(roomId, cancellationToken))
            return (false, "Room must have at least one valid seat before creating a showtime.", null);

        DateTime endTime;
        try
        {
            endTime = startTime.AddMinutes(checked(movie.DurationMin + 30));
        }
        catch (ArgumentOutOfRangeException)
        {
            return (false, "StartTime and movie duration exceed the supported date range", null);
        }

        var normalizedStatus = existing is null
            ? CalculateStatus(startTime, DateTime.UtcNow)
            : manualStatus ?? existing.Status;

        var (succeeded, errorMsg, savedShowtime) = await ExecuteInTransactionAsync(async () =>
        {
            var cinemaIds = existing is null || existing.RoomID == roomId
                ? [room.CinemaID]
                : new[] { existing.Room.CinemaID, room.CinemaID }.Distinct().Order().ToArray();
            foreach (var cinemaId in cinemaIds)
            {
                if (!await _showtimeRepository.AcquireCinemaScheduleLockAsync(cinemaId, cancellationToken))
                    return (false, "Cinema not found", (Showtime?)null);
            }

            var roomIds = existing is null ? [roomId] : new[] { existing.RoomID, roomId }.Distinct().Order().ToArray();
            foreach (var id in roomIds)
            {
                if (!await _showtimeRepository.AcquireRoomScheduleLockAsync(id, cancellationToken))
                    return (false, "Room not found", (Showtime?)null);
            }

            if (normalizedStatus != "cancelled"
                && await _showtimeRepository.HasConflictAsync(
                    roomId, startTime, endTime, existing?.ShowtimeID, cancellationToken))
                return (false, "Showtime conflicts with another showtime in the same room", (Showtime?)null);

            if (normalizedStatus != "cancelled"
                && await _showtimeRepository.HasRoomTypeStartConflictAsync(
                    room.CinemaID, room.RoomTypeID, startTime, existing?.ShowtimeID, cancellationToken))
                return (false,
                    "Another showtime with the same room type already starts at this time in the cinema",
                    (Showtime?)null);

            var showtime = existing ?? new Showtime { CreatedAt = DateTime.UtcNow };
            showtime.MovieID = movieId;
            showtime.RoomID = roomId;
            showtime.StartTime = startTime;
            showtime.EndTime = endTime;
            showtime.BasePrice = basePrice;
            if (existing is null || existing.RoomID != roomId)
                showtime.RoomExtraPrice = room.RoomType.ExtraPrice;
            showtime.Status = normalizedStatus!;
            var saved = existing is null
                ? await _showtimeRepository.AddAsync(showtime, cancellationToken)
                : await _showtimeRepository.UpdateAsync(showtime, cancellationToken);
            return saved is null
                ? (false, "Showtime not found", (Showtime?)null)
                : (true, (string?)null, saved);
        }, cancellationToken);

        if (succeeded && savedShowtime is not null)
        {
            var action = existing is null ? "created" : "updated";
            await _notificationOutbox.EnqueueShowtimeUpdatedAsync(savedShowtime.ShowtimeID,
                $"Showtime for {movie.Title} in {room.RoomName} at {startTime:HH:mm dd/MM/yyyy} has been {action}.",
                cancellationToken);
        }
        return (succeeded, errorMsg, savedShowtime);
    }

    private Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) =>
        _unitOfWork is null ? operation() : _unitOfWork.ExecuteInTransactionAsync(operation, cancellationToken);

    private static string CalculateStatus(DateTime startTime, DateTime now) =>
        now < startTime ? "scheduled" : "completed";

    public string GetDisplayStatus(Showtime showtime, DateTime now)
    {
        if (showtime.Status == "cancelled")
            return "cancelled";

        return now < showtime.EndTime ? "scheduled" : "completed";
    }

    public bool IsActive(Showtime showtime, DateTime now) =>
        showtime.Status != "cancelled" && now < showtime.EndTime;

    private static string? NormalizeManualStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var result = EnumValueMapper.Validate(status, "Status", DatabaseEnumMappings.ShowtimeStatuses);
        return result.Succeeded ? result.DatabaseValue : InvalidManualStatus;
    }

    public Task<Showtime?> GetShowtimeByIdAsync(
        int id, CancellationToken cancellationToken = default) =>
        _showtimeRepository.GetShowtimeByIdAsync(id, cancellationToken);

    public Task<Showtime?> GetManagedShowtimeByIdAsync(
        int id, CancellationToken cancellationToken = default) =>
        _showtimeRepository.GetManagedShowtimeByIdAsync(id, cancellationToken);

    public async Task<(SeatMapResult? SeatMap, string? ErrorMessage)> GetSeatMapAsync(
        int showtimeId,
        int? viewerCinemaId = null,
        CancellationToken cancellationToken = default)
    {
        var showtime = await _showtimeRepository.GetShowtimeByIdAsync(showtimeId, cancellationToken);
        if (showtime is null) return (null, "Showtime not found.");
        if (viewerCinemaId.HasValue && showtime.Room.CinemaID != viewerCinemaId.Value)
            return (null, CinemaScopeMessages.AccessDenied);
        if (showtime.Status != "scheduled")
            return (null, "This showtime is not scheduled and its seat map is unavailable.");
        if (showtime.Room.Status != "active")
            return (null, "The showtime room is inactive.");
        if (showtime.Room.Cinema.Status != "active")
            return (null, "The showtime cinema is inactive.");

        var now = DateTime.UtcNow;
        var seats = await _showtimeRepository.GetSeatsByRoomAsync(showtime.RoomID, cancellationToken);
        var bookedSeatIds = await _showtimeRepository.GetBookedSeatIdsAsync(showtimeId, cancellationToken);
        var heldSeatIds = await _showtimeRepository.GetHeldSeatIdsAsync(showtimeId, now, cancellationToken);
        var seatResults = seats.Select(seat => new SeatMapSeatResult(
            seat.SeatID, seat.SeatRow, seat.SeatCol, seat.SeatType?.TypeName,
            seat.SeatType?.ExtraPrice ?? 0,
            seat.IsGap ? 0 : showtime.BasePrice + showtime.RoomExtraPrice + (seat.SeatType?.ExtraPrice ?? 0),
            seat.IsGap ? seat.Status : bookedSeatIds.Contains(seat.SeatID) ? SeatStatus.Booked
                : heldSeatIds.Contains(seat.SeatID) ? SeatStatus.Held : SeatStatus.Available,
            seat.IsGap)).ToList();
        return (new SeatMapResult(showtime.ShowtimeID, showtime.Room.RoomName, showtime.Room.RoomType.TypeName, seatResults), null);
    }
}
