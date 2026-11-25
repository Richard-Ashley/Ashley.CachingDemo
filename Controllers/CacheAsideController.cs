using System.Text.Json;
using Ashley.CachingDemo.Models;
using Ashley.CachingDemo.Services;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Ashley.CachingDemo.Controllers;

/// <summary>
/// Cache-aside pattern with explicit invalidation on writes.
/// Reads check cache first, fall through to source on miss, write-back to cache.
/// Writes bypass the cache but invalidate the stale entry.
/// </summary>
[ApiController]
[Route("api/cache-aside/products")]
[Produces("application/json")]
public sealed class CacheAsideController : ControllerBase
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProductSource _source;
    private readonly IConnectionMultiplexer _redis;

    public CacheAsideController(IProductSource source, IConnectionMultiplexer redis)
    {
        _source = source;
        _redis = redis;
    }

    /// <summary>Returns a product by id. Cache-aside read path.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetById(int id, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var key = $"cache-aside:product:{id}";

        var cached = await db.StringGetAsync(key);
        if (cached.HasValue)
        {
            var hit = JsonSerializer.Deserialize<Product>((string)cached!, JsonOptions);
            return hit is null ? NotFound() : Ok(hit);
        }

        var product = await _source.GetProduct(id, cancellationToken);
        if (product is null)
            return NotFound();

        await db.StringSetAsync(
            key,
            JsonSerializer.Serialize(product, JsonOptions),
            CacheTtl);

        return Ok(product);
    }

    /// <summary>
    /// Updates a product. Writes go to the source first, then invalidate the cache entry.
    /// The next read will see the fresh data.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _source.GetProduct(id, cancellationToken);
        if (existing is null)
            return NotFound();

        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            Description = request.Description ?? existing.Description,
            Price = request.Price ?? existing.Price,
            Stock = request.Stock ?? existing.Stock
        };

        await _source.UpdateProduct(updated, cancellationToken);

        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"cache-aside:product:{id}");

        return NoContent();
    }

    /// <summary>Clears this controller's cached entries.</summary>
    [HttpPost("flush")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Flush()
    {
        var db = _redis.GetDatabase();
        for (var i = 1; i <= 100; i++)
        {
            await db.KeyDeleteAsync($"cache-aside:product:{i}");
        }
        return NoContent();
    }
}

public sealed record UpdateProductRequest(
    string? Name,
    string? Description,
    decimal? Price,
    int? Stock);
