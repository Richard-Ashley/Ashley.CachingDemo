using Ashley.CachingDemo.Models;

namespace Ashley.CachingDemo.Services;

public interface IProductSource
{
    Task<Product?> GetProduct(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> GetAllProducts(CancellationToken cancellationToken = default);

    Task UpdateProduct(Product product, CancellationToken cancellationToken = default);
}
