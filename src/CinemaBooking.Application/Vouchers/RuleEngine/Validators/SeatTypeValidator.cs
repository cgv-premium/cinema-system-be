using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Vouchers.RuleEngine.Validators;

/// <summary>
/// Validates SeatType rules - all seats must match required type
/// </summary>
public sealed class SeatTypeValidator : IVoucherRuleValidator
{
    public string RuleType => VoucherRuleTypes.SeatType;

    public ValidationResult Validate(VoucherRule rule, VoucherValidationContext context)
    {
        var requiredSeatType = rule.RuleValue;
        var seats = context.Seats;

        if (!seats.Any())
        {
            return ValidationResult.Failure(
                RuleType,
                "This voucher requires seat selection and cannot be applied to F&B-only orders.");
        }

        var invalidSeats = seats
            .Where(s => s.SeatType != requiredSeatType)
            .ToList();

        if (invalidSeats.Any())
        {
            return ValidationResult.Failure(
                RuleType,
                $"Voucher is only valid for {requiredSeatType} seats.");
        }

        return ValidationResult.Success(0);
    }
}
