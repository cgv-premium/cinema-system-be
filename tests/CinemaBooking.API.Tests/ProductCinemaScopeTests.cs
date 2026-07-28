using CinemaBooking.API.Controllers;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Products;
using Microsoft.AspNetCore.Authorization;

namespace CinemaBooking.API.Tests;

public sealed class ProductGlobalTests
{
    [Theory]
    [InlineData(nameof(ProductController.GetProducts))]
    [InlineData(nameof(ProductController.GetProductById))]
    public void ProductReadEndpoints_AllowAnonymousAccess(string methodName)
    {
        var method = typeof(ProductController).GetMethod(methodName)!;

        Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    [Fact]
    public void Product_DoesNotHaveCinemaOrStockFields()
    {
        var property = typeof(Product).GetProperty("CinemaID");

        Assert.Null(property);
        Assert.Null(typeof(Product).GetProperty("StockQuantity"));
        Assert.Null(typeof(Product).GetProperty("IsOnMenu"));
    }

    [Fact]
    public void AvailableProducts_DoesNotRequireCinemaId()
    {
        var method = typeof(ProductController).GetMethod(
            nameof(ProductController.GetAvailableProducts))!;

        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name == "cinemaId");
    }

    [Fact]
    public void ProductController_OnlyRequiresProductService()
    {
        var constructor = Assert.Single(typeof(ProductController).GetConstructors());

        Assert.Equal(typeof(IProductService), Assert.Single(constructor.GetParameters()).ParameterType);
    }

    [Fact]
    public async Task CreateProduct_DefaultsToActive()
    {
        var repository = new StubProductRepository();
        var service = new ProductService(repository, new StubImageStorageService());

        var result = await service.CreateProductAsync(
            "Popcorn", "snack", null, 50_000, null, false);

        Assert.True(result.Succeeded);
        Assert.Equal("active", result.Product!.Status);
    }

    [Fact]
    public async Task UpdateProduct_InactiveStatus_IsAccepted()
    {
        var repository = new StubProductRepository
        {
            ExistingProduct = new Product { ItemID = 1, ItemName = "Popcorn" }
        };
        var service = new ProductService(repository, new StubImageStorageService());

        var result = await service.UpdateProductAsync(
            1, "Popcorn", "snack", null, 50_000, null, false, "inactive");

        Assert.True(result.Succeeded);
        Assert.Equal("inactive", result.Product!.Status);
        Assert.True(repository.UpdateCalled);
    }

    [Fact]
    public async Task UpdateProductImage_ValidImage_StoresCloudinaryDetails()
    {
        var repository = new StubProductRepository
        {
            ExistingProduct = new Product { ItemID = 1, ItemName = "Popcorn" }
        };
        var imageStorage = new StubImageStorageService();
        var service = new ProductService(repository, imageStorage);

        await using var stream = new MemoryStream([1, 2, 3]);
        var result = await service.UpdateProductImageAsync(
            1, stream, "popcorn.png", "image/png", stream.Length);

        Assert.True(result.Succeeded);
        Assert.Equal("https://example.com/products/popcorn.png", result.Product!.ImageURL);
        Assert.Equal("products/popcorn", result.Product.ImagePublicId);
        Assert.Equal("products", imageStorage.UploadFolder);
    }

    [Fact]
    public async Task UpdateProductImage_WhenDatabaseUpdateFails_PreservesExistingImage()
    {
        var repository = new StubProductRepository
        {
            ExistingProduct = new Product
            {
                ItemID = 1,
                ItemName = "Popcorn",
                ImageURL = "https://example.com/products/old.png",
                ImagePublicId = "products/old"
            },
            ThrowOnImageUpdate = true
        };
        var imageStorage = new StubImageStorageService();
        var service = new ProductService(repository, imageStorage);

        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateProductImageAsync(
            1, stream, "popcorn.png", "image/png", stream.Length));

        Assert.DoesNotContain("products/old", imageStorage.DeletedPublicIds);
        Assert.Contains("products/popcorn", imageStorage.DeletedPublicIds);
    }

    [Fact]
    public async Task UpdateProduct_WhenImageUrlChanges_DeletesOldManagedImage()
    {
        var repository = new StubProductRepository
        {
            ExistingProduct = new Product
            {
                ItemID = 1, ItemName = "Popcorn", ImageURL = "old-url",
                ImagePublicId = "products/old"
            }
        };
        var storage = new StubImageStorageService();
        var service = new ProductService(repository, storage);

        var result = await service.UpdateProductAsync(
            1, "Popcorn", "snack", null, 50_000, "new-url", false, "active");

        Assert.True(result.Succeeded);
        Assert.Contains("products/old", storage.DeletedPublicIds);
    }

    [Fact]
    public async Task DeleteUnusedProduct_DeletesManagedImageAfterDatabaseDelete()
    {
        var repository = new StubProductRepository
        {
            ExistingProduct = new Product { ItemID = 1, ImagePublicId = "products/old" }
        };
        var storage = new StubImageStorageService();
        var service = new ProductService(repository, storage);

        var result = await service.DeleteProductAsync(1);

        Assert.True(result.Succeeded);
        Assert.Contains("products/old", storage.DeletedPublicIds);
    }

    private sealed class StubProductRepository : IProductRepository
    {
        public Product? ExistingProduct { get; init; }
        public bool UpdateCalled { get; private set; }
        public bool ThrowOnImageUpdate { get; init; }

        public Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Product>());
        public Task<List<Product>> GetAvailableProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Product>());
        public Task<Product?> GetByIdAsync(
            int itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingProduct);
        public Task<List<Product>> GetProductsByIdsAsync(
            List<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Product>());
        public Task<bool> NameExistsAsync(
            string itemName, int? excludingItemId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
        public Task<Product> AddAsync(
            Product product, CancellationToken cancellationToken = default) =>
            Task.FromResult(product);
        public Task<Product?> UpdateAsync(
            Product product, CancellationToken cancellationToken = default)
        {
            UpdateCalled = true;
            return Task.FromResult<Product?>(product);
        }
        public Task<Product?> UpdateImageAsync(
            int itemId, string imageUrl, string imagePublicId,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnImageUpdate)
                throw new InvalidOperationException("Database update failed.");

            if (ExistingProduct is null || ExistingProduct.ItemID != itemId)
                return Task.FromResult<Product?>(null);

            ExistingProduct.ImageURL = imageUrl;
            ExistingProduct.ImagePublicId = imagePublicId;
            return Task.FromResult<Product?>(ExistingProduct);
        }
        public Task<bool> IsUsedInBookingsAsync(
            int itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> DeleteAsync(
            int itemId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class StubImageStorageService : IImageStorageService
    {
        public string? UploadFolder { get; private set; }
        public List<string> DeletedPublicIds { get; } = [];

        public Task<StoredImageResult> UploadImageAsync(
            Stream imageStream,
            string fileName,
            string folder,
            CancellationToken cancellationToken = default)
        {
            UploadFolder = folder;
            return Task.FromResult(new StoredImageResult(
                "https://example.com/products/popcorn.png",
                "products/popcorn"));
        }

        public Task DeleteImageAsync(
            string publicId,
            CancellationToken cancellationToken = default)
        {
            DeletedPublicIds.Add(publicId);
            return Task.CompletedTask;
        }
    }
}
