using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Vouchers.RuleEngine.Validators;

/// <summary>
/// Validates Room rules
/// </summary>
public sealed class RoomValidator : IVoucherRuleValidator
{
    public string RuleType => VoucherRuleTypes.Room;

    public ValidationResult Validate(VoucherRule rule, VoucherValidationContext context)
    {
        if (context.RoomId == 0)
        {
            return ValidationResult.Failure(
                RuleType,
                "This voucher requires a specific room and cannot be applied to F&B-only orders.");
        }

        var requiredRoomTypeId = rule.RuleValue;
        var bookingRoomTypeId = context.RoomTypeId.ToString();

        if (requiredRoomTypeId != bookingRoomTypeId)
        {
            return ValidationResult.Failure(
                RuleType,
                "Voucher is not valid for this room.");
        }

        return ValidationResult.Success(0);
    }
}
