using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Vouchers.RuleEngine.Validators;

/// <summary>
/// Validates Movie rules
/// </summary>
public sealed class MovieValidator : IVoucherRuleValidator
{
    public string RuleType => VoucherRuleTypes.Movie;

    public ValidationResult Validate(VoucherRule rule, VoucherValidationContext context)
    {
        if (context.MovieId == 0)
        {
            return ValidationResult.Failure(
                RuleType,
                "This voucher requires a movie ticket and cannot be applied to F&B-only orders.");
        }

        var requiredMovieId = rule.RuleValue;
        var bookingMovieId = context.MovieId.ToString();

        if (requiredMovieId != bookingMovieId)
        {
            return ValidationResult.Failure(
                RuleType,
                "Voucher is not valid for this movie.");
        }

        return ValidationResult.Success(0);
    }
}
