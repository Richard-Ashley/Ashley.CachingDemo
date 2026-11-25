using System.Text.Json;
using Ashley.CachingDemo.Models;
using Ashley.CachingDemo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace Ashley.CachingDemo.Controllers;

/// <summary>
/// Endpoints cached with IDistributedCache backed by Redis.
/// Survives app restart, shared across all instances behind a load balancer.
/// </summary>
[ApiController]
[Route("api/distributed/products")]
[Produces("application/json")]
public sealed class DistributedCacheController : ControllerBase
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProductSource _source;
    private readonly IDistributedCache _cache;

    public DistributedCacheController(IProductSource source, IDistributedCache cache)
    {
        _source = source;
        _cache = cache;
    }

    /// <summary>
    /// Returns a product by id. First call hits the source (~500ms); subsequent calls
    /// deserialize from Redis (~2-5ms including network round-trip to localhost).
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetById(int id, CancellationToken cancellationToken)
    {
        var key = $"distributed:product:{id}";

        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (cached is not null)
        {
            var hit = JsonSerializer.Deserialize<Product>(cached, JsonOptions);
            return hit is null ? NotFound() : Ok(hit);
        }

        var product = await _source.GetProduct(id, cancellationToken);
        if (product is null)
            return NotFound();

        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(product, JsonOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            },
            cancellationToken);

        return Ok(product);
    }

    /// <summary>
    /// Returns all products. Same pattern as GetById but with the collection.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Product>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Product>>> GetAll(CancellationToken cancellationToken)
    {
        const string key = "distributed:products:all";

        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (cached is not null)
        {
            var hit = JsonSerializer.Deserialize<IReadOnlyList<Product>>(cached, JsonOptions);
            return Ok(hit ?? []);
        }

        var products = await _source.GetAllProducts(cancellationToken);

        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(products, JsonOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            },
            cancellationToken);

        return Ok(products);
    }

    /// <summary>
    /// Clears this controller's cached entries from Redis.
    /// </summary>
    [HttpPost("flush")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Flush(CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync("distributed:products:all", cancellationToken);
        for (var i = 1; i <= 100; i++)
        {
            await _cache.RemoveAsync($"distributed:product:{i}", cancellationToken);
        }
        return NoContent();
    }
}
