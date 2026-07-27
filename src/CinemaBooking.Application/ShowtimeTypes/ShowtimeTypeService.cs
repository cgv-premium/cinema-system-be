using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Time;

namespace CinemaBooking.Application.ShowtimeTypes;

public sealed class ShowtimeTypeService(IShowtimeTypeRepository repository) : IShowtimeTypeService
{
    private const int MaxSlots = 15;
    public async Task<(bool, string?, ShowtimeTypePage?)> ListAsync(int? cinemaId, bool? active, int page, int pageSize, int? scope, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100) return (false, "Page must be >= 1 and pageSize between 1 and 100.", null);
        if (scope.HasValue && cinemaId.HasValue && cinemaId != scope) return (false, "Access denied.", null);
        return (true, null, await repository.ListAsync(scope ?? cinemaId, active, page, pageSize, ct));
    }
    public async Task<ShowtimeTypeWriteResult> GetAsync(int id, int? scope, CancellationToken ct)
    { var x = await repository.GetAsync(id, false, ct); return x is null ? new(false, "Showtime type not found.", null) : scope.HasValue && x.CinemaID != scope ? new(false, "Access denied.", null) : new(true, null, x); }
    public async Task<ShowtimeTypeWriteResult> CreateAsync(int cinemaId, string name, IReadOnlyList<TimeSpan> slots, int? scope, CancellationToken ct)
    {
        var error = await ValidateTemplate(cinemaId, name, slots, null, scope, ct); if (error is not null) return new(false, error, null);
        var now = DateTime.UtcNow; var value = new ShowtimeType { CinemaID = cinemaId, Name = name.Trim(), CreatedAt = now, UpdatedAt = now, Slots = slots.Distinct().Order().Select(x => new ShowtimeTypeSlot { StartTime = x }).ToList() };
        await repository.AddAsync(value, ct); return new(true, null, value);
    }
    public async Task<ShowtimeTypeWriteResult> UpdateAsync(int id, string name, bool active, IReadOnlyList<TimeSpan> slots, int? scope, CancellationToken ct)
    {
        var value = await repository.GetAsync(id, true, ct); if (value is null) return new(false, "Showtime type not found.", null); if (scope.HasValue && value.CinemaID != scope) return new(false, "Access denied.", null);
        var error = await ValidateTemplate(value.CinemaID, name, slots, id, scope, ct); if (error is not null) return new(false, error, null);
        value.Name = name.Trim(); value.IsActive = active; value.UpdatedAt = DateTime.UtcNow; repository.ReplaceSlots(value, slots.Distinct().Order()); await repository.SaveAsync(ct); return new(true, null, value);
    }
    public async Task<ShowtimeTypeWriteResult> DeleteAsync(int id, int? scope, CancellationToken ct)
    { var value = await repository.GetAsync(id, true, ct); if (value is null) return new(false, "Showtime type not found.", null); if (scope.HasValue && value.CinemaID != scope) return new(false, "Access denied.", null); value.IsActive = false; value.UpdatedAt = DateTime.UtcNow; await repository.SaveAsync(ct); return new(true, null, value); }
    public Task<ShowtimeTypeBatchResult> PreviewAsync(int m, int r, DateOnly s, DateOnly e, int t, decimal p, int? scope, CancellationToken ct) => BuildAsync(m, r, s, e, t, p, scope, false, ct);
    public Task<ShowtimeTypeBatchResult> GenerateAsync(int m, int r, DateOnly s, DateOnly e, int t, decimal p, int? scope, CancellationToken ct) => repository.TransactionAsync(async () => { var room = await repository.GetRoomAsync(r, ct); if (room is not null) await repository.LockScheduleAsync(r, room.CinemaID, ct); return await BuildAsync(m, r, s, e, t, p, scope, true, ct); }, ct);
    private async Task<ShowtimeTypeBatchResult> BuildAsync(int movieId, int roomId, DateOnly start, DateOnly end, int typeId, decimal price, int? scope, bool save, CancellationToken ct)
    {
        if (start == default) return Fail("startDate is required.");
        if (end == default) return Fail("endDate is required.");
        if (start > end) return Fail("startDate must not be later than endDate.");
        if (end.DayNumber - start.DayNumber + 1 > 31) return Fail("Date range must not exceed 31 days.");
        if (start < VietnamTime.GetDate(DateTime.UtcNow)) return Fail("startDate must not be in the past.");
        if (price < 0) return Fail("basePrice must be greater than or equal to 0.");
        var type = await repository.GetAsync(typeId, false, ct); if (type is null) return Fail("Showtime type not found.");
        var movie = await repository.GetMovieAsync(movieId, ct); if (movie is null) return Fail("Movie not found.");
        var room = await repository.GetRoomAsync(roomId, ct); if (room is null) return Fail("Room not found.");
        if (!await repository.HasValidSeatAsync(roomId, ct)) return Fail("Room must have at least one valid seat before creating a showtime.");
        if (scope.HasValue && room.CinemaID != scope || type.CinemaID != room.CinemaID) return Fail("Access denied.");
        var items = new List<ShowtimeTypeItem>(); var accepted = new List<(DateTime Start, DateTime End)>();
        for (var date = start; date <= end; date = date.AddDays(1)) foreach (var slot in type.Slots.OrderBy(x => x.StartTime))
        {
            var begin = VietnamTime.ToUtc(date, TimeOnly.FromTimeSpan(slot.StartTime)); var finish = begin.AddMinutes(movie.DurationMin + 30); string? code = null; string? reason = null;
            if (!type.IsActive) (code, reason) = ("TEMPLATE_INACTIVE", "Showtime type is inactive.");
            else if (room.Status != "active" || room.Cinema.Status != "active") (code, reason) = ("ROOM_INACTIVE", "Room and cinema must be active.");
            else if (begin <= DateTime.UtcNow.AddMinutes(30)) (code, reason) = ("START_TIME_TOO_SOON", "Showtime must start at least 30 minutes from now.");
            else if (movie.DurationMin <= 0 || movie.Status is not ("now_showing" or "coming_soon")) (code, reason) = ("MOVIE_INVALID", "Movie is not eligible for scheduling.");
            else if (accepted.Any(x => begin < x.End && finish > x.Start)) (code, reason) = ("SELF_OVERLAP", "Overlap within generated showtimes.");
            else if (await repository.HasConflictAsync(roomId, begin, finish, ct)) (code, reason) = ("ROOM_OVERLAP", "Overlap with existing showtime in the same room.");
            else if (movie.ShowingFrom.HasValue && date < movie.ShowingFrom) (code, reason) = ("MOVIE_NOT_RELEASED", "Movie has not been released.");
            if (code is null) accepted.Add((begin, finish)); items.Add(new(date, begin, finish, code is not null, code, reason, code is null ? (save ? "generated" : "valid") : "skipped"));
        }
        if (save && accepted.Count > 0) await repository.AddShowtimesAsync(accepted.Select(x => new Showtime { MovieID = movieId, RoomID = roomId, ShowtimeTypeID = typeId, StartTime = x.Start, EndTime = x.End, BasePrice = price, Status = "scheduled", CreatedAt = DateTime.UtcNow }), ct);
        return new(true, null, items, accepted.Count, items.Count - accepted.Count);
    }
    private async Task<string?> ValidateTemplate(int cinemaId, string name, IReadOnlyList<TimeSpan> slots, int? except, int? scope, CancellationToken ct)
    { if (scope.HasValue && cinemaId != scope) return "Access denied."; if (!await repository.CinemaExistsAsync(cinemaId, ct)) return "Cinema not found."; if (string.IsNullOrWhiteSpace(name)) return "name is required."; if (name.Trim().Length > 100) return "name must not exceed 100 characters."; if (slots is null || slots.Count == 0) return "slots must contain at least 1 time."; if (slots.Count > MaxSlots) return "slots must not contain more than 15 times."; if (slots.Distinct().Count() != slots.Count) return "slots must not contain duplicate times."; if (slots.Any(x => x < TimeSpan.Zero || x >= TimeSpan.FromDays(1))) return "Each slot must be a valid time within a day."; return await repository.NameExistsAsync(cinemaId, name.Trim(), except, ct) ? "Showtime type name already exists in this cinema." : null; }
    private static ShowtimeTypeBatchResult Fail(string error) => new(false, error, [], 0, 0);
}
