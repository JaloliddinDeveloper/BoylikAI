using BoylikAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace BoylikAI.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(80);
    private const int LockMaxRetries = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase(); // Har safar yangi instance (Redis cluster safe)

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var value = await Db.StringGetAsync(key);
            if (!value.HasValue) return null;
            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache GET failed for key {Key}", key);
            return null; // Cache miss kabi davom etadi
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null,
        CancellationToken ct = default) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await Db.StringSetAsync(key, json, expiry ?? DefaultExpiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache SET failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await Db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache DELETE failed for key {Key}", key);
        }
    }

    /// <summary>
    /// KEYS buyrug'i o'rniga SCAN ishlatiladi.
    /// KEYS O(N) — serverni bloklaydi. SCAN iterativ va non-blocking.
    /// </summary>
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var server = GetWritableServer();
            if (server is null)
            {
                _logger.LogWarning("No writable Redis server found for prefix removal");
                return;
            }

            var db = Db;
            var batch = new List<RedisKey>(100);

            // SCAN — non-blocking, iterativ qidirish
            await foreach (var key in server.KeysAsync(pattern: $"{prefix}*", pageSize: 100))
            {
                batch.Add(key);
                if (batch.Count >= 100)
                {
                    await db.KeyDeleteAsync(batch.ToArray());
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await db.KeyDeleteAsync(batch.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache prefix DELETE failed for prefix {Prefix}", prefix);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try { return await Db.KeyExistsAsync(key); }
        catch { return false; }
    }

    /// <summary>
    /// Cache stampede (thundering herd) himoyasi:
    /// Distributed lock bilan faqat bitta process factory'ni chaqiradi.
    /// Boshqa processlar lock bo'shashini kutadi, so'ng cache'dan oladi.
    /// </summary>
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiry = null,
        CancellationToken ct = default) where T : class
    {
        // 1. Cache'dan olishga urinish
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        // 2. Distributed lock olish — cache stampede oldini olish
        var lockKey = $"lock:{key}";
        var lockValue = Guid.NewGuid().ToString("N");
        var db = Db;

        for (var attempt = 0; attempt < LockMaxRetries; attempt++)
        {
            // Atomic lock: SET NX EX
            var acquired = await db.StringSetAsync(
                lockKey, lockValue, LockExpiry, When.NotExists);

            if (acquired)
            {
                try
                {
                    // Lock olindi — factory'ni chaqiramiz
                    var value = await factory(ct);
                    await SetAsync(key, value, expiry, ct);
                    return value;
                }
                finally
                {
                    // Faqat o'z lockimizni o'chiramiz (Lua atomic script)
                    await ReleaseLockAsync(db, lockKey, lockValue);
                }
            }

            // Lock boshqa jarayonda — kutib, cache'ni tekshiramiz
            await Task.Delay(LockRetryDelay, ct);
            cached = await GetAsync<T>(key, ct);
            if (cached is not null) return cached;
        }

        // Max retry oshdi — to'g'ridan-to'g'ri factory chaqiramiz (cache bypass)
        _logger.LogWarning("Distributed lock max retries reached for key {Key}, bypassing cache", key);
        return await factory(ct);
    }

    private IServer? GetWritableServer() =>
        _redis.GetServers().FirstOrDefault(s => s.IsConnected && !s.IsReplica);

    // Atomic lock release — Lua script orqali (race condition bo'lmaydi)
    private static readonly string ReleaseLockScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    private async Task ReleaseLockAsync(IDatabase db, string lockKey, string lockValue)
    {
        try
        {
            await db.ScriptEvaluateAsync(
                ReleaseLockScript,
                keys: [(RedisKey)lockKey],
                values: [(RedisValue)lockValue]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release lock for key {LockKey}", lockKey);
        }
    }
}
