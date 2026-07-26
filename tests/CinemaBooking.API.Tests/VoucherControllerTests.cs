using System.Security.Claims;
using CinemaBooking.API.Contracts.Vouchers;
using CinemaBooking.API.Controllers;
using CinemaBooking.Application.Vouchers;
using CinemaBooking.Application.Vouchers.RuleEngine.Metadata;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.API.Tests;

public sealed class VoucherControllerTests
{
    // ============ CREATE PUBLIC VOUCHER ============

    [Fact]
    public async Task Create_PublicVoucherValid_ReturnsCreated()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "SUMMER10",
            DiscountType = "percent",
            DiscountValue = 10,
            MinOrderValue = 100_000,
            MaxUses = 100,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            Description = "Summer promotion",
            IsActive = true,
            IsRedeemable = false,
            RequiredPoints = null,
            ExchangeLimit = null
        };

        var voucher = new Voucher
        {
            VoucherID = 1,
            VoucherCode = request.VoucherCode,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            IsActive = request.IsActive,
            IsRedeemable = request.IsRedeemable,
            CreatedAt = DateTime.UtcNow
        };

        var controller = CreateController(new VoucherResult(true, null, voucher));
        var result = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
    }

    [Fact]
    public async Task Create_PublicVoucherFixedDiscount_ReturnsCreated()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "FLAT5K",
            DiscountType = "fixed",
            DiscountValue = 5_000,
            MinOrderValue = 50_000,
            MaxUses = 200,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = false
        };

        var voucher = new Voucher { VoucherID = 1, VoucherCode = request.VoucherCode, IsRedeemable = false };
        var controller = CreateController(new VoucherResult(true, null, voucher));

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<CreatedResult>(result);
    }

    // ============ CREATE LOYALTY VOUCHER ============

    [Fact]
    public async Task Create_LoyaltyVoucherValid_ReturnsCreated()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "LOYALTY100",
            DiscountType = "percent",
            DiscountValue = 10,
            MaxUses = 1000,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = true,
            RequiredPoints = 100,
            ExchangeLimit = 5
        };

        var voucher = new Voucher
        {
            VoucherID = 1,
            VoucherCode = request.VoucherCode,
            IsRedeemable = true,
            RequiredPoints = 100,
            ExchangeLimit = 5
        };

        var controller = CreateController(new VoucherResult(true, null, voucher));
        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<CreatedResult>(result);
    }

    // ============ VALIDATION: DISCOUNT VALUE ============

    [Fact]
    public async Task Create_PercentDiscountZero_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 0,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = false
        };

        var controller = CreateController(
            new VoucherResult(false, "Discount percent must be between 0 (exclusive) and 100 (inclusive).", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task Create_PercentDiscountAbove100_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 150,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = false
        };

        var controller = CreateController(
            new VoucherResult(false, "Discount percent must be between 0 (exclusive) and 100 (inclusive).", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_FixedDiscountZero_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "fixed",
            DiscountValue = 0,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = false
        };

        var controller = CreateController(
            new VoucherResult(false, "Discount amount must be greater than 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_FixedDiscountNegative_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "fixed",
            DiscountValue = -1000,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = false
        };

        var controller = CreateController(
            new VoucherResult(false, "Discount amount must be greater than 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ============ VALIDATION: DISCOUNT TYPE ============

    [Fact]
    public async Task Create_InvalidDiscountType_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "cashback",
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = false
        };

        var controller = CreateController(
            new VoucherResult(false, "Discount type must be either 'percent' or 'fixed'.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ============ VALIDATION: VALID DATES ============

    [Fact]
    public async Task Create_ValidFromAfterValidUntil_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = false
        };

        var controller = CreateController(
            new VoucherResult(false, "ValidFrom must be before ValidUntil.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ============ VALIDATION: PUBLIC VS LOYALTY ============

    [Fact]
    public async Task Create_PublicVoucherWithRequiredPoints_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = false,
            RequiredPoints = 100
        };

        var controller = CreateController(
            new VoucherResult(false, "Public voucher must have requiredPoints as null.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_PublicVoucherWithExchangeLimit_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = false,
            ExchangeLimit = 5
        };

        var controller = CreateController(
            new VoucherResult(false, "Public voucher must have exchangeLimit as null.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_LoyaltyVoucherMissingRequiredPoints_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            MaxUses = 100,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = true,
            ExchangeLimit = 5
        };

        var controller = CreateController(
            new VoucherResult(false, "Loyalty voucher must have requiredPoints greater than 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_LoyaltyVoucherMissingExchangeLimit_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            MaxUses = 100,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = true,
            RequiredPoints = 100
        };

        var controller = CreateController(
            new VoucherResult(false, "Loyalty voucher must have exchangeLimit greater than 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_LoyaltyVoucherMissingMaxUses_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = true,
            RequiredPoints = 100,
            ExchangeLimit = 5
        };

        var controller = CreateController(
            new VoucherResult(false, "Loyalty voucher must have maxUses greater than 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ============ VALIDATION: DATABASE CONFLICTS ============

    [Fact]
    public async Task Create_DuplicateVoucherCode_ReturnsConflict()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "DUPLICATE",
            DiscountType = "percent",
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7))
        };

        var controller = CreateController(
            new VoucherResult(false, "Voucher code already exists.", null, "conflict")
        );

        var result = await controller.Create(request, CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
    }

    // Helper method
    private static VoucherController CreateController(VoucherResult createResult)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("userId", "1"),
            new Claim(ClaimTypes.Role, Roles.Admin)
        ], "Test");

        // Projection is null because Create/Update paths tested here never invoke it.
        return new VoucherController(new StubVoucherService(createResult), new StubVoucherRuleMetadataProvider(), null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private sealed class StubVoucherRuleMetadataProvider : IVoucherRuleMetadataProvider
    {
        public IReadOnlyList<VoucherRuleTypeMetadata> GetAll() => [];
    }

    // ============ UPDATE OPERATIONS ============

    [Fact]
    public async Task Update_PublicVoucherValid_ReturnsOk()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "SUMMER10",
            DiscountType = "percent",
            DiscountValue = 15,
            MaxUses = 150,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(7)),
            IsActive = true,
            IsRedeemable = false
        };

        var updatedVoucher = new Voucher
        {
            VoucherID = 1,
            VoucherCode = request.VoucherCode,
            DiscountValue = request.DiscountValue,
            MaxUses = request.MaxUses
        };

        var controller = CreateController(new VoucherResult(true, null, updatedVoucher));
        var result = await controller.Update(1, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Update_VoucherNotFound_ReturnsNotFound()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "SUMMER10",
            DiscountType = "percent",
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7))
        };

        var controller = CreateController(
            new VoucherResult(false, "Voucher not found.", null, "not_found")
        );

        var result = await controller.Update(999, request, CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task Update_ReduceMaxUsesBelowUsedCount_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "SUMMER10",
            DiscountType = "percent",
            DiscountValue = 10,
            MaxUses = 5,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7))
        };

        var controller = CreateController(
            new VoucherResult(false, "MaxUses cannot be less than current UsedCount.", null)
        );

        var result = await controller.Update(1, request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ============ EDGE CASES: MINORDERVALUE ============

    [Fact]
    public async Task Create_MinOrderValueNegative_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            MinOrderValue = -1000,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7))
        };

        var controller = CreateController(
            new VoucherResult(false, "Minimum order value must be greater than or equal to 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_MinOrderValueZero_ReturnsCreated()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "NOMIN",
            DiscountType = "percent",
            DiscountValue = 5,
            MinOrderValue = 0,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7))
        };

        var voucher = new Voucher { VoucherID = 1, VoucherCode = request.VoucherCode };
        var controller = CreateController(new VoucherResult(true, null, voucher));

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<CreatedResult>(result);
    }

    // ============ EDGE CASES: DATES ============

    [Fact]
    public async Task Create_ValidFromEqualsValidUntil_ReturnsBadRequest()
    {
        var sameDate = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7));
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            ValidFrom = sameDate,
            ValidUntil = sameDate
        };

        var controller = CreateController(
            new VoucherResult(false, "ValidFrom must be before ValidUntil.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ============ EDGE CASES: LOYALTY CONSTRAINTS ============

    [Fact]
    public async Task Create_LoyaltyVoucherRequiredPointsZero_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            MaxUses = 100,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = true,
            RequiredPoints = 0,
            ExchangeLimit = 5
        };

        var controller = CreateController(
            new VoucherResult(false, "Loyalty voucher must have requiredPoints greater than 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_LoyaltyVoucherExchangeLimitZero_ReturnsBadRequest()
    {
        var request = new VoucherRequest
        {
            VoucherCode = "INVALID",
            DiscountType = "percent",
            DiscountValue = 10,
            MaxUses = 100,
            ValidFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            ValidUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            IsRedeemable = true,
            RequiredPoints = 100,
            ExchangeLimit = 0
        };

        var controller = CreateController(
            new VoucherResult(false, "Loyalty voucher must have exchangeLimit greater than 0.", null)
        );

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private sealed class StubVoucherService : IVoucherService
    {
        private readonly VoucherResult _createResult;

        public StubVoucherService(VoucherResult createResult) => _createResult = createResult;

        public Task<VoucherPage> GetAsync(string? search, int pageIndex, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<VoucherResult> CreateAsync(int adminId, VoucherCommand command, string? ip, CancellationToken ct) =>
            Task.FromResult(_createResult);

        public Task<VoucherResult> UpdateAsync(int adminId, int id, VoucherCommand command, string? ip, CancellationToken ct) =>
            Task.FromResult(_createResult);

        public Task<VoucherResult> DeleteAsync(int adminId, int id, string? ip, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RedeemableVouchersResult> GetRedeemableVouchersAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RedeemVoucherResult> RedeemVoucherAsync(int userId, int voucherId, string? ip, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<UserVouchersResult> GetUserVouchersAsync(int userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<UserRedeemableVouchersResult> GetUserRedeemableVouchersAsync(int userId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
