using Microsoft.Extensions.Caching.Distributed;
using Polly;
using Polly.RateLimiting;
using Polly.Timeout;

namespace QuotesApi.Caching;

// Wraps the real Redis-backed IDistributedCache (StackExchangeRedisCache,
// registered by AddStackExchangeRedisCache in InfrastructureExtensions.cs)
// with a Polly resilience pipeline. This is what HybridCache actually
// resolves as its L2 - see InfrastructureExtensions.cs for the DI wiring
// that makes that swap. See day-22/README.md for the full pipeline
// design and the composition-order reasoning.
//
// Every IDistributedCache operation - Get, Set, Refresh, Remove - is
// naturally idempotent: a cache's whole contract is "read/write/delete by
// key," and repeating any of those has the same observable effect as
// doing it once (re-setting a key to the same value, or deleting an
// already-deleted key, changes nothing). That's what makes it safe to
// apply retry uniformly across all four operations here, unlike the
// zenquotes.io pipeline, which has to reason about idempotency at the
// level of "this HTTP method" rather than get it for free - see
// RandomQuoteClientExtensions.cs.
public class ResilientDistributedCache : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger _logger;

    public ResilientDistributedCache(IDistributedCache inner, ResiliencePipeline pipeline, ILogger logger)
    {
        _inner = inner;
        _pipeline = pipeline;
        _logger = logger;
    }

    // Bulkhead rejections and per-attempt timeouts are caught here (not
    // swallowed - rethrown after logging) specifically so they're logged
    // as what they actually are. From the outside both eventually surface
    // to HybridCache as "the L2 call failed," but they mean different
    // things operationally - too much concurrent load vs. a single slow
    // call - and conflating them in the logs is exactly what this task
    // asked not to do. HybridCache's own broad exception handling around
    // L2 (proven in day-21/README.md, and again for BrokenCircuitException
    // in day-22's live verification) is what keeps the request itself
    // degrading gracefully - this rethrow doesn't change that, it only
    // adds a log line on the way past.
    private T ExecuteSync<T>(Func<CancellationToken, T> action, string operation)
    {
        try
        {
            return _pipeline.Execute(action);
        }
        catch (RateLimiterRejectedException)
        {
            _logger.LogWarning("Redis {Operation} rejected by bulkhead - too many concurrent calls in flight.", operation);
            throw;
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning("Redis {Operation} timed out after {Timeout}.", operation, ex.Timeout);
            throw;
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken token, string operation)
    {
        try
        {
            return await _pipeline.ExecuteAsync(action, token);
        }
        catch (RateLimiterRejectedException)
        {
            _logger.LogWarning("Redis {Operation} rejected by bulkhead - too many concurrent calls in flight.", operation);
            throw;
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning("Redis {Operation} timed out after {Timeout}.", operation, ex.Timeout);
            throw;
        }
    }

    public byte[]? Get(string key) =>
        ExecuteSync(_ => _inner.Get(key), nameof(Get));

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        await ExecuteAsync(async ct => await _inner.GetAsync(key, ct), token, nameof(GetAsync));

    public void Refresh(string key) =>
        ExecuteSync(_ =>
        {
            _inner.Refresh(key);
            return true;
        }, nameof(Refresh));

    public async Task RefreshAsync(string key, CancellationToken token = default) =>
        await ExecuteAsync(async ct =>
        {
            await _inner.RefreshAsync(key, ct);
            return true;
        }, token, nameof(RefreshAsync));

    public void Remove(string key) =>
        ExecuteSync(_ =>
        {
            _inner.Remove(key);
            return true;
        }, nameof(Remove));

    public async Task RemoveAsync(string key, CancellationToken token = default) =>
        await ExecuteAsync(async ct =>
        {
            await _inner.RemoveAsync(key, ct);
            return true;
        }, token, nameof(RemoveAsync));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        ExecuteSync(_ =>
        {
            _inner.Set(key, value, options);
            return true;
        }, nameof(Set));

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        await ExecuteAsync(async ct =>
        {
            await _inner.SetAsync(key, value, options, ct);
            return true;
        }, token, nameof(SetAsync));
}
