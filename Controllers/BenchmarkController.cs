using System.Diagnostics;
using Ashley.CachingDemo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace Ashley.CachingDemo.Controllers;

/// <summary>
/// Measures warm cache hits across all four caching strategies against an uncached baseline.
/// Returns latency in milliseconds for each, plus a short note explaining what was measured.
/// </summary>
[ApiController]
[Route("api/benchmark")]
[Produces("application/json")]
public sealed class BenchmarkController : ControllerBase
{
    private const int BenchmarkProductId = 1;

    private readonly IProductSource _source;
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly IOutputCacheStore _outputCacheStore;
    private readonly IHttpClientFactory _httpClientFactory;

    public BenchmarkController(
        IProductSource source,
        IMemoryCache memoryCache,
        IDistributedCache distributedCache,
        IConnectionMultiplexer redis,
        IOutputCacheStore outputCacheStore,
        IHttpClientFactory httpClientFactory)
    {
        _source = source;
        _memoryCache = memoryCache;
        _distributedCache = distributedCache;
        _redis = redis;
        _outputCacheStore = outputCacheStore;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Runs one cold uncached call plus one warm hit per caching strategy.
    /// Returns a comparison of latencies in milliseconds.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(BenchmarkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<BenchmarkResult>> Run(CancellationToken cancellationToken)
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";
        var http = _httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(baseUri);

        var uncached = await Measure(async () =>
        {
            await _source.GetProduct(BenchmarkProductId, cancellationToken);
        });

        var memory = await MeasureViaHttp(http, "/api/memory/products/1", warmup: true, cancellationToken);
        var distributed = await MeasureViaHttp(http, "/api/distributed/products/1", warmup: true, cancellationToken);
        var cacheAside = await MeasureViaHttp(http, "/api/cache-aside/products/1", warmup: true, cancellationToken);
        var output = await MeasureViaHttp(http, "/api/output/products/1", warmup: true, cancellationToken);

        return Ok(new BenchmarkResult(
            Uncached: new BenchmarkEntry(uncached, "Baseline: source service called directly, no caching."),
            MemoryCache: new BenchmarkEntry(memory, "In-process IMemoryCache hit."),
            DistributedCache: new BenchmarkEntry(distributed, "Redis-backed IDistributedCache hit via localhost."),
            CacheAside: new BenchmarkEntry(cacheAside, "Cache-aside pattern hit via direct StackExchange.Redis."),
            OutputCache: new BenchmarkEntry(output, "ASP.NET Core output caching hit; handler never invoked.")));
    }

    /// <summary>
    /// Resets every strategy's cache. Useful for running the benchmark from a cold state.
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        if (_memoryCache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }

        await _distributedCache.RemoveAsync($"distributed:product:{BenchmarkProductId}", cancellationToken);
        await _distributedCache.RemoveAsync("distributed:products:all", cancellationToken);

        var redisDb = _redis.GetDatabase();
        await redisDb.KeyDeleteAsync($"cache-aside:product:{BenchmarkProductId}");

        await _outputCacheStore.EvictByTagAsync("products", cancellationToken);

        return NoContent();
    }

    private static async Task<double> Measure(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static async Task<double> MeasureViaHttp(HttpClient client, string path, bool warmup, CancellationToken cancellationToken)
    {
        if (warmup)
        {
            using var warmResponse = await client.GetAsync(path, cancellationToken);
            warmResponse.EnsureSuccessStatusCode();
        }

        var sw = Stopwatch.StartNew();
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }
}

public sealed record BenchmarkResult(
    BenchmarkEntry Uncached,
    BenchmarkEntry MemoryCache,
    BenchmarkEntry DistributedCache,
    BenchmarkEntry CacheAside,
    BenchmarkEntry OutputCache);

public sealed record BenchmarkEntry(double Ms, string Note);
