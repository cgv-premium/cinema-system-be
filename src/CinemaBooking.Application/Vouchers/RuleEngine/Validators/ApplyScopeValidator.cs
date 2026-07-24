using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Vouchers.RuleEngine.Validators;

/// <summary>
/// Validates ApplyScope rules and calculates applicable amount
/// </summary>
public sealed class ApplyScopeValidator : IVoucherRuleValidator
{
    public string RuleType => VoucherRuleTypes.ApplyScope;

    public ValidationResult Validate(VoucherRule rule, VoucherValidationContext context)
    {
        var scope = rule.RuleValue;

        if (scope != ApplyScopes.Order && scope != ApplyScopes.Ticket && scope != ApplyScopes.Food)
        {
            return ValidationResult.Failure(
                RuleType,
                $"Invalid ApplyScope value: {scope}. Must be Order, Ticket, or Food.");
        }

        var applicableAmount = CalculateApplicableAmount(scope, context);

        if (applicableAmount <= 0)
        {
            if (scope == ApplyScopes.Ticket)
            {
                return ValidationResult.Failure(
                    RuleType,
                    "This voucher applies only to movie tickets and cannot be applied to F&B-only orders.");
            }

            return ValidationResult.Failure(
                RuleType,
                $"Voucher applies to {scope} but booking has no applicable amount in that category.");
        }

        return ValidationResult.Success(applicableAmount);
    }

    private static decimal CalculateApplicableAmount(string scope, VoucherValidationContext context)
    {
        return scope switch
        {
            ApplyScopes.Order => context.BookingTotal,
            ApplyScopes.Ticket => context.TicketTotal,
            ApplyScopes.Food => context.FoodTotal,
            _ => 0
        };
    }
}
