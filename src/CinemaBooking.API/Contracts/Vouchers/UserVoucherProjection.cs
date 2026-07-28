using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Vouchers;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;
using CinemaBooking.Shared.Time;

namespace CinemaBooking.API.Contracts.Vouchers;

// Shared projection for endpoints that return a customer's owned loyalty vouchers
// as one aggregated card per VoucherID (with Quantity = usable copies).
// Consumed by /api/vouchers/my-vouchers and /api/users/lookup so both endpoints
// have identical grouping, rule mapping, and batched name-lookup behavior.
public sealed class UserVoucherProjection
{
    private readonly IMovieRepository _movieRepository;
    private readonly ICinemaRepository _cinemaRepository;
    private readonly ILoyaltyRepository _loyaltyRepository;
    private readonly IRoomTypeRepository _roomTypeRepository;
    private readonly IProductRepository _productRepository;

    public UserVoucherProjection(
        IMovieRepository movieRepository,
        ICinemaRepository cinemaRepository,
        ILoyaltyRepository loyaltyRepository,
        IRoomTypeRepository roomTypeRepository,
        IProductRepository productRepository)
    {
        _movieRepository = movieRepository;
        _cinemaRepository = cinemaRepository;
        _loyaltyRepository = loyaltyRepository;
        _roomTypeRepository = roomTypeRepository;
        _productRepository = productRepository;
    }

    // Group by VoucherID, keeping only USABLE copies (Available and not past ExpiredAt).
    // Groups with zero usable copies are dropped entirely. Representative row is the
    // newest usable copy by RedeemedAt.
    public async Task<List<UserVoucherResponse>> ProjectAsync(
        IEnumerable<UserVoucher> userVouchers,
        CancellationToken ct)
    {
        var (grouped, _, movieNames, cinemaNames, tierNames, roomTypeNames, productNames) =
            await GroupUsableAndLookupNamesAsync(userVouchers, ct);

        return grouped
            .Select(x => MapUserVoucher(x.representative, x.quantity, movieNames, cinemaNames, tierNames, roomTypeNames, productNames))
            .ToList();
    }

    // Same grouping semantics as ProjectAsync but returns the richer MyVoucherResponse
    // shape (full voucher metadata) used only by /api/vouchers/my-vouchers.
    public async Task<List<MyVoucherResponse>> ProjectMyVouchersAsync(
        IEnumerable<UserVoucher> userVouchers,
        CancellationToken ct)
    {
        var (grouped, now, movieNames, cinemaNames, tierNames, roomTypeNames, productNames) =
            await GroupUsableAndLookupNamesAsync(userVouchers, ct);

        return grouped
            .Select(x => MapMyVoucher(x.representative, x.quantity, now, movieNames, cinemaNames, tierNames, roomTypeNames, productNames))
            .ToList();
    }

    // Shared preamble for the two owned-voucher projections: filter → group by
    // VoucherID → pick representative → batch rule-name lookups. Returning `now`
    // lets callers that need current-time-dependent fields (e.g. lifecycle status)
    // reuse the exact same reference point used for the ExpiredAt filter.
    private async Task<(
        List<(UserVoucher representative, int quantity)> grouped,
        DateTime now,
        Dictionary<int, string> movieNames,
        Dictionary<int, string> cinemaNames,
        Dictionary<int, string> tierNames,
        Dictionary<int, string> roomTypeNames,
        Dictionary<int, string> productNames)>
        GroupUsableAndLookupNamesAsync(IEnumerable<UserVoucher> userVouchers, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var grouped = userVouchers
            .Where(uv => uv.Status == UserVoucherStatus.Available && uv.ExpiredAt >= now)
            .GroupBy(uv => uv.VoucherID)
            .Select(g =>
            {
                var representative = g.OrderByDescending(uv => uv.RedeemedAt).First();
                return (representative, quantity: g.Count());
            })
            .OrderByDescending(x => x.representative.RedeemedAt)
            .ToList();

        var (movieNames, cinemaNames, tierNames, roomTypeNames, productNames) = await BuildRuleNameLookupsAsync(
            grouped.Select(x => x.representative.Voucher), ct);

        return (grouped, now, movieNames, cinemaNames, tierNames, roomTypeNames, productNames);
    }

    // Build a redeemable-voucher card list from the vouchers themselves (no ownership
    // info). Used by /api/vouchers/redeemable.
    public async Task<List<RedeemableVoucherResponse>> ProjectRedeemableAsync(
        IEnumerable<Voucher> vouchers,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var (movieNames, cinemaNames, tierNames, roomTypeNames, productNames) = await BuildRuleNameLookupsAsync(vouchers, ct);
        return vouchers.Select(v => MapRedeemable(v, now, movieNames, cinemaNames, tierNames, roomTypeNames, productNames)).ToList();
    }

    private async Task<(Dictionary<int, string> movieNames, Dictionary<int, string> cinemaNames, Dictionary<int, string> tierNames, Dictionary<int, string> roomTypeNames, Dictionary<int, string> productNames)>
        BuildRuleNameLookupsAsync(IEnumerable<Voucher> vouchers, CancellationToken ct)
    {
        var movieIds = new HashSet<int>();
        var cinemaIds = new HashSet<int>();
        var tierIds = new HashSet<int>();
        var roomTypeIds = new HashSet<int>();
        var productIds = new HashSet<int>();

        foreach (var voucher in vouchers)
        {
            foreach (var rule in voucher.VoucherRules ?? [])
            {
                if (rule.RuleType == "Movie" && int.TryParse(rule.RuleValue, out var movieId))
                    movieIds.Add(movieId);
                else if (rule.RuleType == "Cinema" && int.TryParse(rule.RuleValue, out var cinemaId))
                    cinemaIds.Add(cinemaId);
                else if (rule.RuleType == "Membership" && int.TryParse(rule.RuleValue, out var tierId))
                    tierIds.Add(tierId);
                else if (rule.RuleType == "Room" && int.TryParse(rule.RuleValue, out var roomTypeId))
                    roomTypeIds.Add(roomTypeId);
                else if (rule.RuleType == "Product" && int.TryParse(rule.RuleValue, out var productId))
                    productIds.Add(productId);
            }
        }

        var movieNames = movieIds.Any()
            ? (await _movieRepository.GetMoviesByIdsAsync(movieIds.ToList(), ct))
                .ToDictionary(m => m.MovieID, m => m.Title)
            : new Dictionary<int, string>();

        var cinemaNames = cinemaIds.Any()
            ? (await _cinemaRepository.GetCinemasByIdsAsync(cinemaIds.ToList(), ct))
                .ToDictionary(c => c.CinemaID, c => c.CinemaName)
            : new Dictionary<int, string>();

        var tierNames = tierIds.Any()
            ? (await _loyaltyRepository.GetTiersByIdsAsync(tierIds.ToList(), ct))
                .ToDictionary(t => t.TierID, t => t.TierName)
            : new Dictionary<int, string>();

        var roomTypeNames = roomTypeIds.Any()
            ? (await _roomTypeRepository.GetRoomTypesByIdsAsync(roomTypeIds.ToList(), ct))
                .ToDictionary(r => r.RoomTypeID, r => r.TypeName)
            : new Dictionary<int, string>();

        var productNames = productIds.Any()
            ? (await _productRepository.GetProductsByIdsAsync(productIds.ToList(), ct))
                .ToDictionary(p => p.ItemID, p => p.ItemName)
            : new Dictionary<int, string>();

        return (movieNames, cinemaNames, tierNames, roomTypeNames, productNames);
    }

    private static List<RedeemableVoucherRuleResponse> MapVoucherRules(
        Voucher v,
        Dictionary<int, string> movieNames,
        Dictionary<int, string> cinemaNames,
        Dictionary<int, string> tierNames,
        Dictionary<int, string> roomTypeNames,
        Dictionary<int, string> productNames) =>
        (v.VoucherRules ?? [])
            .Select(r => new RedeemableVoucherRuleResponse(
                r.RuleType,
                GetOperatorForRuleType(r.RuleType),
                r.RuleValue,
                RedeemableVoucherRuleDisplayTextGenerator.GenerateDisplayText(
                    r.RuleType,
                    r.RuleValue,
                    movieNames,
                    cinemaNames,
                    null,
                    tierNames,
                    roomTypeNames,
                    productNames)))
            .ToList();

    // Populates the metadata block that is identical between MyVoucherResponse
    // and UserVoucherResponse. Only the `Status` field differs between the two
    // callers, so each mapping method builds this once and then plugs in its
    // own status value below.
    private static (
        int VoucherId, string VoucherCode, string DiscountType, decimal DiscountValue,
        decimal? MinOrderValue, int? MaxUses, int UsedCount,
        DateTimeOffset ValidFrom, DateTimeOffset ValidUntil,
        string? ImageUrl, string? Description,
        bool IsActive, DateTime CreatedAt,
        List<RedeemableVoucherRuleResponse> VoucherRules,
        bool IsRedeemable, int? RequiredPoints, int? ExchangeLimit,
        int Quantity,
        DateTimeOffset RedeemedAt, DateTimeOffset ExpiredAt, DateTimeOffset? UsedAt)
        BuildVoucherFields(
            UserVoucher uv,
            int quantity,
            Dictionary<int, string> movieNames,
            Dictionary<int, string> cinemaNames,
            Dictionary<int, string> tierNames,
            Dictionary<int, string> roomTypeNames,
            Dictionary<int, string> productNames)
    {
        var v = uv.Voucher;
        return (
            v.VoucherID, v.VoucherCode, v.DiscountType, v.DiscountValue,
            v.MinOrderValue, v.MaxUses, v.UsedCount,
            VietnamTime.FromUtc(v.ValidFrom), VietnamTime.FromUtc(v.ValidUntil),
            v.ImageURL, v.Description,
            v.IsActive, v.CreatedAt,
            MapVoucherRules(v, movieNames, cinemaNames, tierNames, roomTypeNames, productNames),
            v.IsRedeemable, v.RequiredPoints, v.ExchangeLimit,
            quantity,
            VietnamTime.FromUtc(uv.RedeemedAt), VietnamTime.FromUtc(uv.ExpiredAt),
            uv.UsedAt.HasValue ? VietnamTime.FromUtc(uv.UsedAt.Value) : null);
    }

    // Ownership status (available/used/expired) — used by /api/users/lookup.
    private static UserVoucherResponse MapUserVoucher(
        UserVoucher uv,
        int quantity,
        Dictionary<int, string> movieNames,
        Dictionary<int, string> cinemaNames,
        Dictionary<int, string> tierNames,
        Dictionary<int, string> roomTypeNames,
        Dictionary<int, string> productNames)
    {
        var f = BuildVoucherFields(uv, quantity, movieNames, cinemaNames, tierNames, roomTypeNames, productNames);
        return new UserVoucherResponse(
            f.VoucherId, f.VoucherCode, f.DiscountType, f.DiscountValue,
            f.MinOrderValue, f.MaxUses, f.UsedCount,
            f.ValidFrom, f.ValidUntil,
            f.ImageUrl, f.Description,
            f.IsActive, uv.Status, f.CreatedAt,
            f.VoucherRules,
            f.IsRedeemable, f.RequiredPoints, f.ExchangeLimit,
            f.Quantity, f.RedeemedAt, f.ExpiredAt, f.UsedAt);
    }

    // Voucher lifecycle status (ACTIVE/EXPIRED/…) — used by /api/vouchers/my-vouchers.
    private static MyVoucherResponse MapMyVoucher(
        UserVoucher uv,
        int quantity,
        DateTime currentTime,
        Dictionary<int, string> movieNames,
        Dictionary<int, string> cinemaNames,
        Dictionary<int, string> tierNames,
        Dictionary<int, string> roomTypeNames,
        Dictionary<int, string> productNames)
    {
        var f = BuildVoucherFields(uv, quantity, movieNames, cinemaNames, tierNames, roomTypeNames, productNames);
        return new MyVoucherResponse(
            f.VoucherId, f.VoucherCode, f.DiscountType, f.DiscountValue,
            f.MinOrderValue, f.MaxUses, f.UsedCount,
            f.ValidFrom, f.ValidUntil,
            f.ImageUrl, f.Description,
            f.IsActive, VoucherLifecycleStatus(uv.Voucher, currentTime), f.CreatedAt,
            f.VoucherRules,
            f.IsRedeemable, f.RequiredPoints, f.ExchangeLimit,
            f.Quantity, f.RedeemedAt, f.ExpiredAt, f.UsedAt);
    }

    // Canonical DISABLED/EXPIRED/EXHAUSTED/UPCOMING/ACTIVE lifecycle string used
    // by both the admin voucher list and /api/vouchers/my-vouchers so the two
    // views can never drift apart.
    public static string VoucherLifecycleStatus(Voucher v, DateTime currentTime)
    {
        if (!v.IsActive) return "DISABLED";
        if (currentTime > v.ValidUntil) return "EXPIRED";
        if (v.MaxUses.HasValue && v.UsedCount >= v.MaxUses.Value) return "EXHAUSTED";
        if (currentTime < v.ValidFrom) return "UPCOMING";
        return "ACTIVE";
    }

    private static RedeemableVoucherResponse MapRedeemable(
        Voucher v,
        DateTime currentTime,
        Dictionary<int, string> movieNames,
        Dictionary<int, string> cinemaNames,
        Dictionary<int, string> tierNames,
        Dictionary<int, string> roomTypeNames,
        Dictionary<int, string> productNames) => new(
        v.VoucherID,
        v.VoucherCode,
        v.DiscountType,
        v.DiscountValue,
        v.MinOrderValue,
        v.MaxUses,
        v.UsedCount,
        VietnamTime.FromUtc(v.ValidFrom),
        VietnamTime.FromUtc(v.ValidUntil),
        v.ImageURL,
        v.Description,
        v.IsActive,
        VoucherLifecycleStatus(v, currentTime),
        v.CreatedAt,
        MapVoucherRules(v, movieNames, cinemaNames, tierNames, roomTypeNames, productNames),
        v.IsRedeemable,
        v.RequiredPoints!.Value,
        v.ExchangeLimit,
        // Ownership-only fields — always null for a catalog entry
        Quantity: null,
        RedeemedAt: null,
        ExpiredAt: null,
        UsedAt: null);

    private static string GetOperatorForRuleType(string ruleType) => ruleType switch
    {
        "MinimumSpend" => ">=",
        "MaximumSpend" => "<=",
        "TicketQuantity" => ">=",
        "Movie" => "=",
        "Cinema" => "=",
        "SeatType" => "=",
        "Room" => "=",
        "Membership" => "=",
        "PaymentMethod" => "=",
        "DayOfWeek" => "=",
        "Product" => "=",
        "FoodCategory" => "=",
        "FoodAndDrink" => "=",
        "ApplyScope" => "=",
        _ => "="
    };
}
