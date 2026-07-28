using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Application.Vouchers.RuleEngine;

/// <summary>
/// Result of voucher rule validation
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; set; }
    public string? FailedRule { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal ApplicableAmount { get; set; }

    public static ValidationResult Success(decimal applicableAmount) => new()
    {
        IsValid = true,
        ApplicableAmount = applicableAmount
    };

    public static ValidationResult Failure(string ruleType, string errorMessage) => new()
    {
        IsValid = false,
        FailedRule = ruleType,
        ErrorMessage = errorMessage,
        ApplicableAmount = 0
    };
}

/// <summary>
/// Context containing all data needed for voucher rule validation
/// Validators must NOT query the database - all data is provided in this context
/// </summary>
public sealed class VoucherValidationContext
{
    public int BookingId { get; set; }
    public int? CustomerId { get; set; }
    public int CinemaId { get; set; }
    public int MovieId { get; set; }
    public int RoomId { get; set; }
    public int RoomTypeId { get; set; }
    public DateTime ShowtimeDateTime { get; set; }
    public string? MembershipTier { get; set; }
    public List<SeatValidationData> Seats { get; set; } = [];
    public List<ProductValidationData> Products { get; set; } = [];
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal BookingTotal { get; set; }
    public decimal TicketTotal { get; set; }
    public decimal FoodTotal { get; set; }
    public Voucher Voucher { get; set; } = null!;
    public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
}

public sealed class SeatValidationData
{
    public int SeatID { get; set; }
    public string SeatType { get; set; } = null!;
    public decimal Price { get; set; }
}

public sealed class ProductValidationData
{
    public int ProductID { get; set; }
    public string? Category { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
