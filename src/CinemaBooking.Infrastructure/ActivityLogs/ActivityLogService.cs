using CinemaBooking.Application.ActivityLogs;
using CinemaBooking.Infrastructure.Persistence;
using CinemaBooking.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Infrastructure.ActivityLogs;

public sealed class ActivityLogService : IActivityLogService
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
    private static readonly IReadOnlyDictionary<string, string> Modules = new Dictionary<string, string>
    {
        [AdminActionTypes.CreateUser]="UserManagement", [AdminActionTypes.UpdateUser]="UserManagement",
        [AdminActionTypes.LockUser]="UserManagement", [AdminActionTypes.UnlockUser]="UserManagement",
        [AdminActionTypes.DeactivateUser]="UserManagement",
        [AdminActionTypes.ChangeRole]="UserManagement", [AdminActionTypes.DeleteUser]="UserManagement",
        [AdminActionTypes.CreateVoucher]="Voucher", [AdminActionTypes.UpdateVoucher]="Voucher", [AdminActionTypes.DeleteVoucher]="Voucher",
        [AdminActionTypes.CreateShowtime]="Showtime", [AdminActionTypes.UpdateShowtime]="Showtime", [AdminActionTypes.DeleteShowtime]="Showtime",
        [AdminActionTypes.CreateShowtimeType]="ShowtimeType", [AdminActionTypes.UpdateShowtimeType]="ShowtimeType",
        [AdminActionTypes.DeleteShowtimeType]="ShowtimeType", [AdminActionTypes.GenerateShowtimeByType]="ShowtimeType",
        [AdminActionTypes.UpdateTicketPrice]="TicketPrice", [AdminActionTypes.GenerateSeat]="SeatManagement",
        [AdminActionTypes.UpdateSeat]="SeatManagement", [AdminActionTypes.DeleteSeat]="SeatManagement",
        [AdminActionTypes.CreateCinema]="Cinema", [AdminActionTypes.UpdateCinema]="Cinema", [AdminActionTypes.DeleteCinema]="Cinema",
        [AdminActionTypes.CreateGenre]="Genre", [AdminActionTypes.UpdateGenre]="Genre", [AdminActionTypes.DeleteGenre]="Genre",
        [AdminActionTypes.CreateMovie]="Movie", [AdminActionTypes.UpdateMovie]="Movie", [AdminActionTypes.DeleteMovie]="Movie",
        [AdminActionTypes.ExportReport]="Report", [AdminActionTypes.Refund]="Refund",
        [AdminActionTypes.CreateRoomType]="RoomType", [AdminActionTypes.UpdateRoomType]="RoomType", [AdminActionTypes.DeleteRoomType]="RoomType",
        [AdminActionTypes.CreateSeatType]="SeatType", [AdminActionTypes.UpdateSeatType]="SeatType", [AdminActionTypes.DeleteSeatType]="SeatType",
        [AdminActionTypes.CreateProduct]="Product", [AdminActionTypes.UpdateProduct]="Product", [AdminActionTypes.DeleteProduct]="Product"
    };
    private readonly CinemaBookingDbContext _db;
    public ActivityLogService(CinemaBookingDbContext db) => _db = db;
    public IReadOnlyList<string> GetActionTypes() => Modules.Keys.ToArray();
    public async Task RecordAsync(int actorId, string actionType, string targetTable, int targetId,
        string description, string ipAddress, CancellationToken ct)
    {
        if (!Modules.ContainsKey(actionType)) throw new ArgumentOutOfRangeException(nameof(actionType));
        _db.AdminActionLogs.Add(new AdminActionLog { AdminID=actorId, ActionType=actionType,
            TargetTable=targetTable, TargetID=targetId, Description=description,
            IPAddress=string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress, CreatedAt=DateTime.UtcNow });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ActivityLogPage> GetAsync(string? actionType, string? module, int? actorId, int? targetUserId,
        string? targetTable, int? targetId, DateOnly? startDate, DateOnly? endDate, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.AdminActionLogs.AsNoTracking().Include(x => x.Admin).AsQueryable();
        if (actionType is not null) query = query.Where(x => x.ActionType == actionType);
        if (module is not null) { var actions = Modules.Where(x => x.Value.Equals(module, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToArray(); query = query.Where(x => actions.Contains(x.ActionType)); }
        if (actorId.HasValue) query = query.Where(x => x.AdminID == actorId);
        if (targetUserId.HasValue) query = query.Where(x => x.TargetUserID == targetUserId);
        if (targetTable is not null) query = query.Where(x => x.TargetTable == targetTable);
        if (targetId.HasValue) query = query.Where(x => x.TargetID == targetId);
        if (startDate.HasValue) { var utc = TimeZoneInfo.ConvertTimeToUtc(startDate.Value.ToDateTime(TimeOnly.MinValue), Vietnam); query = query.Where(x => x.CreatedAt >= utc); }
        if (endDate.HasValue) { var utc = TimeZoneInfo.ConvertTimeToUtc(endDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), Vietnam); query = query.Where(x => x.CreatedAt < utc); }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new ActivityLogItem(x.LogID, ToVietnam(x.CreatedAt), new(x.AdminID,x.Admin.FullName,x.Admin.Role),x.ActionType,Module(x.ActionType),x.Description!,x.IPAddress!,x.TargetUserID,x.TargetTable,x.TargetID)).ToArray();
        return new(items,page,pageSize,total,(int)Math.Ceiling(total/(double)pageSize));
    }
    public async Task<ActivityLogDetail?> GetByIdAsync(int id, CancellationToken ct)
    {
        var x = await _db.AdminActionLogs.AsNoTracking().Include(v => v.Admin).SingleOrDefaultAsync(v => v.LogID == id, ct);
        return x is null ? null : new(x.LogID,ToVietnam(x.CreatedAt),x.AdminID,x.Admin.FullName,x.Admin.Role,x.ActionType,Module(x.ActionType),x.IPAddress!,x.TargetUserID,x.TargetTable,x.TargetID,x.Description!);
    }
    private static string Module(string action) => Modules.TryGetValue(action, out var module) ? module : "Unknown";
    private static DateTimeOffset ToVietnam(DateTime value) => TimeZoneInfo.ConvertTime(
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)), Vietnam);
}
