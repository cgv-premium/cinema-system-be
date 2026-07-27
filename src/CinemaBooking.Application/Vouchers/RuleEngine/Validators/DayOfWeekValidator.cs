using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Vouchers.RuleEngine.Validators;

/// <summary>
/// Validates DayOfWeek rules based on showtime
/// </summary>
public sealed class DayOfWeekValidator : IVoucherRuleValidator
{
    public string RuleType => VoucherRuleTypes.DayOfWeek;

    public ValidationResult Validate(VoucherRule rule, VoucherValidationContext context)
    {
        var showtimeDayOfWeek = context.ShowtimeDateTime.DayOfWeek.ToString();

        var allowedDays = (rule.RuleValue ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var isAllowed = allowedDays.Any(day =>
            string.Equals(
                day,
                showtimeDayOfWeek,
                StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            return ValidationResult.Failure(
                RuleType,
                $"Voucher is only valid on {rule.RuleValue}.");
        }

        return ValidationResult.Success(0);
    }
}
