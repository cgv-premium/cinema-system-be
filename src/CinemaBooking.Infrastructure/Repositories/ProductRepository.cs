using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly CinemaBookingDbContext _dbContext;

    public ProductRepository(CinemaBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Products
            .AsNoTracking()
            .OrderBy(p => p.ItemName)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Product>> GetAvailableProductsAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == "active")
            .OrderBy(p => p.ItemName)
            .ToListAsync(cancellationToken);
    }

    public Task<Product?> GetByIdAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ItemID == itemId, cancellationToken);
    }

    public Task<List<Product>> GetProductsByIdsAsync(
        List<int> itemIds,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Products
            .AsNoTracking()
            .Where(p => itemIds.Contains(p.ItemID))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(
        string itemName,
        int? excludingItemId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Products
            .AsNoTracking()
            .Where(p => p.ItemName == itemName);

        if (excludingItemId.HasValue)
        {
            query = query.Where(p => p.ItemID != excludingItemId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<Product> AddAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        product.UpdatedAt = DateTime.UtcNow;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return product;
    }

    public async Task<Product?> UpdateAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        var existingProduct = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.ItemID == product.ItemID, cancellationToken);

        if (existingProduct is null)
        {
            return null;
        }

        existingProduct.ItemName = product.ItemName;
        existingProduct.ItemType = product.ItemType;
        existingProduct.Description = product.Description;
        existingProduct.Price = product.Price;
        if (!string.Equals(existingProduct.ImageURL, product.ImageURL, StringComparison.Ordinal))
            existingProduct.ImagePublicId = null;
        existingProduct.ImageURL = product.ImageURL;
        existingProduct.IsLoyaltyEligible = product.IsLoyaltyEligible;
        existingProduct.Status = product.Status;
        existingProduct.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return existingProduct;
    }

    public async Task<Product?> UpdateImageAsync(
        int itemId,
        string imageUrl,
        string imagePublicId,
        CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.ItemID == itemId, cancellationToken);

        if (product is null)
        {
            return null;
        }

        product.ImageURL = imageUrl;
        product.ImagePublicId = imagePublicId;
        product.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return product;
    }

    public Task<bool> IsUsedInBookingsAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Set<BookingFnB>()
            .AnyAsync(bf => bf.ItemID == itemId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.ItemID == itemId, cancellationToken);

        if (product is null)
        {
            return false;
        }

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
