using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Repositories;

public sealed class VoucherRepository : IVoucherRepository
{
    private readonly CinemaBookingDbContext _db;
    public VoucherRepository(CinemaBookingDbContext db) => _db = db;

    public async Task<(List<Voucher> Items, int Total)> GetPageAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Vouchers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(v => v.VoucherCode.Contains(search)
            || (v.Description != null && v.Description.Contains(search)));
        var total = await query.CountAsync(ct);
        var items = await query
            .Include(v => v.VoucherRules)
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(ct);
        return (items, total);
    }
    public Task<Voucher?> GetByIdAsync(int id, CancellationToken ct) =>
        _db.Vouchers.Include(v => v.VoucherRules).FirstOrDefaultAsync(v => v.VoucherID == id, ct);
    public Task<bool> CodeExistsAsync(string code, int? excludingId, CancellationToken ct) =>
        _db.Vouchers.AnyAsync(v => v.VoucherCode == code && (!excludingId.HasValue || v.VoucherID != excludingId), ct);
    public Task<bool> HasTransactionsAsync(int id, CancellationToken ct) => _db.BookingVouchers.AnyAsync(v => v.VoucherID == id, ct);
    public async Task<Voucher> SaveWithRulesAsync(
        Voucher voucher, bool isNew, IReadOnlyList<VoucherRule> newRules, AdminActionLog log, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            if (isNew)
                _db.Vouchers.Add(voucher);

            // Persist the voucher first so its identity is available for rules and the audit log.
            await _db.SaveChangesAsync(ct);

            if (!isNew)
            {
                var existingRules = await _db.Set<VoucherRule>()
                    .Where(r => r.VoucherID == voucher.VoucherID)
                    .ToListAsync(ct);
                _db.Set<VoucherRule>().RemoveRange(existingRules);
            }

            foreach (var rule in newRules)
                rule.VoucherID = voucher.VoucherID;
            if (newRules.Count > 0)
                await _db.Set<VoucherRule>().AddRangeAsync(newRules, ct);

            log.TargetID = voucher.VoucherID;
            _db.AdminActionLogs.Add(log);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return voucher;
        });
    }
    public async Task<bool> DeactivateAsync(int id, AdminActionLog log, CancellationToken ct)
    {
        var voucher = await _db.Vouchers.FindAsync([id], ct);
        if (voucher is null) return false;
        if (!voucher.IsActive) return true; // idempotent: already deactivated
        voucher.IsActive = false;
        log.TargetID = voucher.VoucherID;
        _db.AdminActionLogs.Add(log);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<Voucher>> GetRedeemableVouchersAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.Vouchers
            .AsNoTracking()
            .Include(v => v.VoucherRules)
            .Where(v => v.IsActive
                && v.IsRedeemable
                && v.RequiredPoints.HasValue
                && v.ValidFrom <= now
                && v.ValidUntil >= now)
            .OrderBy(v => v.RequiredPoints)
            .ToListAsync(ct);
    }

    public async Task<List<Voucher>> GetActiveVouchersAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.Vouchers
            .AsNoTracking()
            .Include(v => v.VoucherRules)
            .Where(v => v.IsActive
                && v.ValidFrom <= now
                && v.ValidUntil >= now)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<Voucher?> GetForRedemptionAsync(int voucherId, CancellationToken ct) =>
        _db.Vouchers.FirstOrDefaultAsync(v => v.VoucherID == voucherId, ct);

    public async Task<int> GetUserRedemptionCountAsync(int userId, int voucherId, CancellationToken ct) =>
        await _db.Set<UserVoucher>().CountAsync(uv => uv.UserID == userId && uv.VoucherID == voucherId, ct);

    public async Task IncrementPublicVoucherUsageForBookingAsync(int bookingId, CancellationToken ct)
    {
        // Single UPDATE gated on IsRedeemable = 0. Loyalty vouchers are skipped by the JOIN
        // predicate, so this method is safe to call for every paid booking regardless of
        // voucher type (or absence of a voucher). The BookingVoucher row is the source of
        // truth for which voucher this booking used.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE v
               SET v.UsedCount = v.UsedCount + 1
              FROM Voucher v
              JOIN BookingVoucher bv ON bv.VoucherID = v.VoucherID
             WHERE bv.BookingID = {bookingId}
               AND v.IsRedeemable = 0", ct);
    }
}
