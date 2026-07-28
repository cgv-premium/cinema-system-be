using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Vouchers.RuleEngine.Metadata;

/// <summary>
/// Central registry describing how the admin UI should render each voucher
/// rule type. Adding a new rule type only requires appending one entry to
/// <see cref="Registry"/> — the endpoint, DI, and API contract stay untouched.
/// </summary>
public sealed class VoucherRuleMetadataProvider : IVoucherRuleMetadataProvider
{
    private static readonly IReadOnlyList<VoucherRuleTypeMetadata> Registry = new[]
    {
        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.ApplyScope,
            DisplayName: "Apply Scope",
            InputType: VoucherRuleInputTypes.Select,
            Options: new[] { ApplyScopes.Order, ApplyScopes.Ticket, ApplyScopes.Food }),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.Cinema,
            DisplayName: "Cinema",
            InputType: VoucherRuleInputTypes.Select,
            DataSource: "/api/cinemas"),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.Movie,
            DisplayName: "Movie",
            InputType: VoucherRuleInputTypes.Select,
            DataSource: "/api/movie"),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.Room,
            DisplayName: "Room",
            InputType: VoucherRuleInputTypes.Select,
            DataSource: "/api/room-types"),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.SeatType,
            DisplayName: "Seat Type",
            InputType: VoucherRuleInputTypes.MultiSelect,
            DataSource: "/api/seat-types"),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.Membership,
            DisplayName: "Membership",
            InputType: VoucherRuleInputTypes.Select,
            DataSource: "/api/membership/tiers"),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.PaymentMethod,
            DisplayName: "Payment Method",
            InputType: VoucherRuleInputTypes.Select,
            Options: new[]
            {
                PaymentMethod.PayOS,
                PaymentMethod.Wallet,
                PaymentMethod.Cash,
                PaymentMethod.Momo,
                PaymentMethod.CreditCard,
                PaymentMethod.Banking
            }),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.DayOfWeek,
            DisplayName: "Day Of Week",
            InputType: VoucherRuleInputTypes.MultiSelect,
            Options: new[]
            {
                nameof(DayOfWeek.Monday),
                nameof(DayOfWeek.Tuesday),
                nameof(DayOfWeek.Wednesday),
                nameof(DayOfWeek.Thursday),
                nameof(DayOfWeek.Friday),
                nameof(DayOfWeek.Saturday),
                nameof(DayOfWeek.Sunday)
            }),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.Product,
            DisplayName: "Product",
            InputType: VoucherRuleInputTypes.Select,
            DataSource: "/api/products"),

        new VoucherRuleTypeMetadata(
            RuleType: VoucherRuleTypes.FoodCategory,
            DisplayName: "Food Category",
            InputType: VoucherRuleInputTypes.Text)
    };

    public IReadOnlyList<VoucherRuleTypeMetadata> GetAll() => Registry;
}
