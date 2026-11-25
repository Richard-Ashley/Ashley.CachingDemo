using Ashley.CachingDemo.Models;
using Ashley.CachingDemo.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ashley.CachingDemo.Controllers;

/// <summary>
/// Baseline endpoints with no caching. Every request pays the full source latency (~500ms).
/// Use these as the comparison point for the cached controllers.
/// </summary>
[ApiController]
[Route("api/uncached/products")]
[Produces("application/json")]
public sealed class UncachedController : ControllerBase
{
    private readonly IProductSource _source;

    public UncachedController(IProductSource source)
    {
        _source = source;
    }

    /// <summary>Returns a product by id. No caching; every call hits the source.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetById(int id, CancellationToken cancellationToken)
    {
        var product = await _source.GetProduct(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>Returns all 100 products. No caching; every call hits the source.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Product>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Product>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await _source.GetAllProducts(cancellationToken);
        return Ok(products);
    }
}
