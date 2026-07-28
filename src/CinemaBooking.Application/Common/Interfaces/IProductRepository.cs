using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Application.Common.Interfaces;

public interface IProductRepository
{
    Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default);

    Task<List<Product>> GetAvailableProductsAsync(CancellationToken cancellationToken = default);

    Task<Product?> GetByIdAsync(int itemId, CancellationToken cancellationToken = default);

    Task<List<Product>> GetProductsByIdsAsync(
        List<int> itemIds,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        string itemName,
        int? excludingItemId = null,
        CancellationToken cancellationToken = default);

    Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default);

    Task<Product?> UpdateAsync(Product product, CancellationToken cancellationToken = default);

    Task<Product?> UpdateImageAsync(
        int itemId,
        string imageUrl,
        string imagePublicId,
        CancellationToken cancellationToken = default);

    Task<bool> IsUsedInBookingsAsync(int itemId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int itemId, CancellationToken cancellationToken = default);
}
