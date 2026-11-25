using Ashley.CachingDemo.Models;

namespace Ashley.CachingDemo.Services;

public sealed class ProductSource : IProductSource
{
    private static readonly TimeSpan SimulatedLatency = TimeSpan.FromMilliseconds(500);

    private readonly List<Product> _products;
    private readonly Lock _lock = new();

    public ProductSource()
    {
        _products = Enumerable.Range(1, 100)
            .Select(i => new Product(
                Id: i,
                Name: $"Product {i}",
                Description: $"A demonstration product with id {i}.",
                Price: Math.Round(10m + (i * 1.7m), 2),
                Stock: 50 + (i % 20),
                UpdatedAt: DateTimeOffset.UtcNow))
            .ToList();
    }

    public async Task<Product?> GetProduct(int id, CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedLatency, cancellationToken);

        lock (_lock)
        {
            return _products.FirstOrDefault(p => p.Id == id);
        }
    }

    public async Task<IReadOnlyList<Product>> GetAllProducts(CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedLatency, cancellationToken);

        lock (_lock)
        {
            return _products.ToList().AsReadOnly();
        }
    }

    public async Task UpdateProduct(Product product, CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedLatency, cancellationToken);

        lock (_lock)
        {
            var index = _products.FindIndex(p => p.Id == product.Id);
            if (index < 0)
                throw new InvalidOperationException($"Product {product.Id} not found.");

            _products[index] = product with { UpdatedAt = DateTimeOffset.UtcNow };
        }
    }
}
