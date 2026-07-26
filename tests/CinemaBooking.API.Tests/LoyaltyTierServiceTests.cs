using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.LoyaltyTiers;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.API.Tests;

public sealed class LoyaltyTierServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidRequest_TrimsName()
    {
        var repository = new StubLoyaltyRepository();
        var service = new LoyaltyTierService(repository);

        var result = await service.CreateAsync(" diamond ", 20000, 0.2m, 9);

        Assert.True(result.Succeeded);
        Assert.Equal("diamond", result.Tier?.TierName);
        Assert.Equal(20000, result.Tier?.MinPoints);
        Assert.Equal(0.2m, result.Tier?.DiscountRate);
        Assert.Equal(9, result.Tier?.MaxRefundPerMonth);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        var repository = new StubLoyaltyRepository { TierNameExists = true };
        var service = new LoyaltyTierService(repository);

        var result = await service.CreateAsync("gold", 20000, 0.2m, 9);

        Assert.False(result.Succeeded);
        Assert.True(result.IsConflict);
        Assert.Equal("Loyalty tier name must be unique.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateMinPoints_IgnoresCurrentTierOnly()
    {
        var repository = new StubLoyaltyRepository
        {
            ExistingTier = new LoyaltyTier { TierID = 2, TierName = "gold", MinPoints = 1000 },
            MinPointsExists = true
        };
        var service = new LoyaltyTierService(repository);

        var result = await service.UpdateAsync(2, "gold plus", 5000, 0.1m, 5);

        Assert.False(result.Succeeded);
        Assert.True(result.IsConflict);
        Assert.Equal(2, repository.ExcludingTierId);
        Assert.Equal("Min points must be unique.", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteAsync_WhenAssignedToUsers_ReturnsConflict()
    {
        var repository = new StubLoyaltyRepository
        {
            ExistingTier = new LoyaltyTier { TierID = 1, TierName = "silver" },
            HasAssignedUsers = true
        };
        var service = new LoyaltyTierService(repository);

        var result = await service.DeleteAsync(1);

        Assert.False(result.Succeeded);
        Assert.True(result.IsConflict);
        Assert.False(repository.DeleteCalled);
    }

    private sealed class StubLoyaltyRepository : ILoyaltyRepository
    {
        public LoyaltyTier? ExistingTier { get; set; }
        public bool TierNameExists { get; set; }
        public bool MinPointsExists { get; set; }
        public bool HasAssignedUsers { get; set; }
        public bool DeleteCalled { get; private set; }
        public int? ExcludingTierId { get; private set; }

        public Task<int> GetUserTotalPointsAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<decimal> GetUserTotalSpentAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoyaltyTier?> GetUserTierAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LoyaltyTier>> GetAllTiersAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<LoyaltyTier>());

        public Task<LoyaltyTier?> GetTierByIdAsync(int tierId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingTier);
        }

        public Task<bool> TierNameExistsAsync(string tierName, int? excludingTierId = null, CancellationToken cancellationToken = default)
        {
            ExcludingTierId = excludingTierId;
            return Task.FromResult(TierNameExists);
        }

        public Task<bool> MinPointsExistsAsync(int minPoints, int? excludingTierId = null, CancellationToken cancellationToken = default)
        {
            ExcludingTierId = excludingTierId;
            return Task.FromResult(MinPointsExists);
        }

        public Task<LoyaltyTier> AddTierAsync(LoyaltyTier tier, CancellationToken cancellationToken = default)
        {
            tier.TierID = 5;
            return Task.FromResult(tier);
        }

        public Task<LoyaltyTier?> UpdateTierAsync(
            int tierId,
            string tierName,
            int minPoints,
            decimal discountRate,
            int maxRefundPerMonth,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LoyaltyTier?>(new LoyaltyTier
            {
                TierID = tierId,
                TierName = tierName,
                MinPoints = minPoints,
                DiscountRate = discountRate,
                MaxRefundPerMonth = maxRefundPerMonth
            });
        }

        public Task<bool> HasAssignedUsersAsync(int tierId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HasAssignedUsers);
        }

        public Task<bool> DeleteTierAsync(int tierId, CancellationToken cancellationToken = default)
        {
            DeleteCalled = true;
            return Task.FromResult(true);
        }

        public Task AddLoyaltyPointAsync(LoyaltyPoints loyaltyPoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LoyaltyPoints>> GetPointHistoryAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateUserTierAsync(int userId, int tierID, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateUserTotalPointsAsync(int userId, int totalPoints, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasPointsForBookingAsync(int bookingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<int>> GetBookingIdsWithEarnedPointsAsync(IReadOnlyCollection<int> bookingIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<LoyaltyTier>> GetTiersByIdsAsync(List<int> tierIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
