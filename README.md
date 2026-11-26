# Ashley.CachingDemo

> Four .NET 10 caching strategies compared side-by-side with live benchmarks.

[![CI](https://github.com/Richard-Ashley/Ashley.CachingDemo/actions/workflows/ci.yml/badge.svg)](https://github.com/Richard-Ashley/Ashley.CachingDemo/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)

A focused demonstration of the four caching strategies most commonly used in production .NET APIs. Each strategy is implemented against the same simulated 500ms source, exposed through its own controller, and measured against an uncached baseline through a benchmark endpoint.

The goal: build the intuition for when each pattern fits, with real numbers from a reproducible setup.

---

## The strategies

| Strategy | Storage | When to use | Trade-off |
|---|---|---|---|
| **IMemoryCache** | In-process dictionary | Reference data on a single server | Lost on restart; does not scale across instances |
| **IDistributedCache (Redis)** | Redis via `IDistributedCache` abstraction | Shared data across multiple app instances | Network round-trip + serialization overhead |
| **Cache-aside** | Redis via raw `IConnectionMultiplexer` | Mutable data that needs explicit invalidation on writes | If you forget to invalidate, stale data until the TTL expires |
| **Output caching** | ASP.NET Core response cache | Public read-heavy endpoints with identical responses | Less granular control; cache lookup happens before the handler |

Plus an **uncached baseline** so the comparison is honest.

---

## Benchmark numbers

The 500 ms baseline is simulated with `Task.Delay`, so the speedup ratios below are a function of that simulated latency. Against a faster real backing store they would be proportionally smaller; the ordering between strategies is the transferable part.

Measured on localhost with Redis in Docker. Each figure is the range seen across six runs of `/api/benchmark`, after the first (warm-up) run. Every run is a single HTTP call per strategy, so expect a few ms of jitter.

| Strategy | Latency (warm hit) | Speedup vs baseline |
|---|---|---|
| Uncached baseline | ~500 ms | 1x |
| IMemoryCache | ~1-2 ms | ~250-500x |
| Output caching | ~1-3 ms | ~170-500x |
| IDistributedCache (Redis) | ~2-4 ms | ~125-250x |
| Cache-aside (raw Redis) | ~2-5 ms | ~100-250x |

The ordering is the useful part: in-process strategies are fastest, and the Redis-backed ones pay a small network and serialization cost per hit. The exact milliseconds will differ on your machine.

Run the benchmark yourself:

```bash
docker compose up -d
dotnet run
curl http://localhost:5211/api/benchmark
```

Returns a JSON object with latency per strategy. `/api/benchmark` needs Redis running: the controller depends on the Redis connection, so it returns a 500 if Redis is down. Failing loudly is deliberate; a broken cache layer should be visible, not papered over.

**Caveats:**
- Each number comes from a single HTTP call. For stable comparisons, take the median of ~20 runs.
- The cached rows include HTTP and JSON overhead; the uncached baseline calls the source service directly. The speedup ratios are therefore conservative.
- Output caching bypasses the handler on a hit, so its latency measures the framework's cache lookup, not any controller work.

---

## Architecture

Single project, deliberately minimal. No layering, no CQRS, no Clean Architecture. The goal is to isolate the caching concept.

```
Ashley.CachingDemo/
├── Models/
│   └── Product.cs                        # sealed record
├── Services/
│   ├── IProductSource.cs                 # simulated slow data source interface
│   └── ProductSource.cs                  # 500ms Task.Delay per call, 100 products
├── Controllers/
│   ├── UncachedController.cs             # baseline (no caching)
│   ├── MemoryCacheController.cs          # IMemoryCache
│   ├── DistributedCacheController.cs     # IDistributedCache + Redis
│   ├── CacheAsideController.cs           # raw IConnectionMultiplexer + invalidation
│   ├── OutputCacheController.cs          # [OutputCache] attribute
│   └── BenchmarkController.cs            # measures all strategies
├── docker-compose.yml                    # Redis 7
├── Program.cs
└── appsettings.json
```

`IProductSource` deliberately sleeps 500ms per call to simulate a slow backing store (a cross-region database, an external API, a heavy aggregation). This makes the caching impact obvious and measurable.

---

## Running locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Redis)

### Steps

```bash
git clone https://github.com/Richard-Ashley/Ashley.CachingDemo.git
cd Ashley.CachingDemo

# Start Redis
docker compose up -d

# Run the API
dotnet run
```

Then open [http://localhost:5211/swagger](http://localhost:5211/swagger) to explore the endpoints. The uncached, memory and output-cache endpoints work without Redis; the distributed, cache-aside and benchmark endpoints need it.

### Endpoint groups

| Route | Strategy |
|---|---|
| `/api/uncached/products/*` | Baseline, no caching |
| `/api/memory/products/*` | `IMemoryCache` |
| `/api/distributed/products/*` | `IDistributedCache` (Redis) |
| `/api/cache-aside/products/*` | Raw Redis + invalidation |
| `/api/output/products/*` | ASP.NET Core output caching |
| `/api/benchmark` | Measures all strategies |

Each cached group has a `POST .../flush` endpoint that clears its cache, and `POST /api/benchmark/reset` flushes the benchmark product from every strategy at once.

### Try the invalidation

```bash
curl -w "%{http_code} %{time_total}s\n" -o /dev/null -s http://localhost:5211/api/cache-aside/products/1   # miss, ~500ms
curl -w "%{http_code} %{time_total}s\n" -o /dev/null -s http://localhost:5211/api/cache-aside/products/1   # hit, a few ms
curl -X PUT http://localhost:5211/api/cache-aside/products/1 -H "Content-Type: application/json" -d '{"price": 999.99}'
curl -w "%{http_code} %{time_total}s\n" -o /dev/null -s http://localhost:5211/api/cache-aside/products/1   # miss again: the write invalidated the entry
```

---

## Design decisions and known limitations

### Why singleton `IProductSource`?

The source holds the current state of 100 products in a private list. Singleton registration keeps that state across requests, which the invalidation demonstration in `CacheAsideController.Update` depends on.

### Why `IDistributedCache` for one controller and raw `IConnectionMultiplexer` for another?

- **`IDistributedCache`** is a .NET abstraction that works against Redis, SQL Server, NCache or in-memory. Portable, but limited to the common subset of operations.
- **`IConnectionMultiplexer`** gives the full Redis API: SCAN, pub/sub, streams, scripts. More powerful, Redis-specific.

Showing both makes the trade-off visible side by side. They also name keys differently: `IDistributedCache` prepends the configured `InstanceName` (`ashley-cachingdemo:`), the raw client does not.

### Single-call measurements

The demo shows the *shape* of the speedup, not statistically rigorous numbers. For real microbenchmarks, use [BenchmarkDotNet](https://benchmarkdotnet.org/). A median-of-N benchmark endpoint would be an easy extension.

### `MemoryCacheController` does not cache 404s

`GetOrCreateAsync<T>` returning `null` is indistinguishable from "not cached", so repeat requests for a missing product pay the full source latency every time. In production, wrap the cached value (`CachedResult<T>` or `Option<T>`) or cache not-founds with a short TTL.

### Cache-aside Redis calls ignore `CancellationToken`

StackExchange.Redis does not accept a `CancellationToken`; a cancelled request still completes its outstanding Redis call. `IDistributedCache` does accept one. A real difference when choosing between them.

### No stampede protection

If N concurrent requests miss on the same key, all N run the factory. Production code usually layers [FusionCache](https://github.com/ZiggyCreatures/FusionCache) or [LazyCache](https://github.com/alastairtree/LazyCache) on top to coalesce concurrent misses.

### Output cache is in-process

The output cache uses the default in-memory store, so it is per-instance. A multi-server setup would need a distributed output-cache store.

---

## Technology stack

| Concern | Choice |
|---|---|
| Runtime / framework | .NET 10, ASP.NET Core 10 |
| Redis client | StackExchange.Redis 2.8.* |
| Distributed cache | Microsoft.Extensions.Caching.StackExchangeRedis 10.0.* |
| API documentation | Swashbuckle.AspNetCore 10.* |
| Redis image | redis:7-alpine |

---

## License

[MIT](LICENSE) © Richard Nii Asuku Ashley
