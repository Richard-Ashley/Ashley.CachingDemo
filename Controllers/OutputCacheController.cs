using Ashley.CachingDemo.Models;
using Ashley.CachingDemo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Ashley.CachingDemo.Controllers;

/// <summary>
/// Endpoints cached with ASP.NET Core output caching.
/// The framework caches the serialized response (status + headers + body) keyed by URL and vary-by rules.
/// Cache lookup happens before the controller runs; on a hit, the handler is never invoked.
/// </summary>
[ApiController]
[Route("api/output/products")]
[Produces("application/json")]
public sealed class OutputCacheController : ControllerBase
{
    private readonly IProductSource _source;
    private readonly IOutputCacheStore _cacheStore;

    public OutputCacheController(IProductSource source, IOutputCacheStore cacheStore)
    {
        _source = source;
        _cacheStore = cacheStore;
    }

    /// <summary>
    /// Returns a product by id. Response cached for 5 minutes, keyed by the id route value.
    /// First call hits the source (~500ms); subsequent calls return cached response (~1ms)
    /// without entering the controller.
    /// </summary>
    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = "ProductsPolicy")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetById(int id, CancellationToken cancellationToken)
    {
        var product = await _source.GetProduct(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>
    /// Returns all products. Response cached for 5 minutes.
    /// </summary>
    [HttpGet]
    [OutputCache(Duration = 300, Tags = new[] { "products" })]
    [ProducesResponseType(typeof(IReadOnlyList<Product>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Product>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await _source.GetAllProducts(cancellationToken);
        return Ok(products);
    }

    /// <summary>
    /// Evicts all output-cached responses tagged "products".
    /// Demonstrates tag-based invalidation, output caching's answer to cache-aside's RemoveAsync.
    /// </summary>
    [HttpPost("flush")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Flush(CancellationToken cancellationToken)
    {
        await _cacheStore.EvictByTagAsync("products", cancellationToken);
        return NoContent();
    }
}
