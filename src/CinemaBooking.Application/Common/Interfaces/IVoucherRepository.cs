using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Application.Common.Interfaces;

public interface IVoucherRepository
{
    Task<(List<Voucher> Items, int Total)> GetPageAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<Voucher?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, int? excludingId, CancellationToken cancellationToken);
    Task<bool> HasTransactionsAsync(int id, CancellationToken cancellationToken);
    /// <summary>
    /// Persists a voucher (insert or update), replaces its rules (delete existing + insert new),
    /// and writes the audit log — all inside ONE transaction. Rolls back on any failure.
    /// </summary>
    Task<Voucher> SaveWithRulesAsync(
        Voucher voucher,
        bool isNew,
        IReadOnlyList<VoucherRule> newRules,
        AdminActionLog log,
        CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(int id, AdminActionLog log, CancellationToken cancellationToken);
    Task<List<Voucher>> GetRedeemableVouchersAsync(CancellationToken cancellationToken);
    Task<List<Voucher>> GetActiveVouchersAsync(CancellationToken cancellationToken);
    Task<Voucher?> GetForRedemptionAsync(int voucherId, CancellationToken cancellationToken);
    Task<int> GetUserRedemptionCountAsync(int userId, int voucherId, CancellationToken cancellationToken);

    /// <summary>
    /// Post-payment counter bump for public vouchers only. Increments Voucher.UsedCount for
    /// the voucher attached to <paramref name="bookingId"/> when Voucher.IsRedeemable is false.
    /// No-op if the booking has no voucher or the voucher is a loyalty voucher (loyalty
    /// UsedCount is bumped at redeem time, not at booking).
    /// </summary>
    Task IncrementPublicVoucherUsageForBookingAsync(int bookingId, CancellationToken cancellationToken);
}
