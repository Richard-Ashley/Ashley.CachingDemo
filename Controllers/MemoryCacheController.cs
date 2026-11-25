using Ashley.CachingDemo.Models;
using Ashley.CachingDemo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Ashley.CachingDemo.Controllers;

/// <summary>
/// Endpoints cached with IMemoryCache (in-process, single server).
/// Perfect for reference data that rarely changes and doesn't need to survive restarts.
/// </summary>
[ApiController]
[Route("api/memory/products")]
[Produces("application/json")]
public sealed class MemoryCacheController : ControllerBase
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IProductSource _source;
    private readonly IMemoryCache _cache;

    public MemoryCacheController(IProductSource source, IMemoryCache cache)
    {
        _source = source;
        _cache = cache;
    }

    /// <summary>
    /// Returns a product by id. First call hits the source (~500ms); subsequent calls
    /// within the TTL return from in-process memory (~1ms).
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetById(int id, CancellationToken cancellationToken)
    {
        var product = await _cache.GetOrCreateAsync(
            $"memory:product:{id}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                return await _source.GetProduct(id, cancellationToken);
            });

        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>
    /// Returns all products. First call hits the source; subsequent calls return cached data.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Product>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Product>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await _cache.GetOrCreateAsync(
            "memory:products:all",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                return await _source.GetAllProducts(cancellationToken);
            });

        return Ok(products);
    }

    /// <summary>
    /// Clears the memory cache. For demonstration — makes it easy to reset between benchmark runs.
    /// </summary>
    [HttpPost("flush")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Flush()
    {
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }
        return NoContent();
    }
}
