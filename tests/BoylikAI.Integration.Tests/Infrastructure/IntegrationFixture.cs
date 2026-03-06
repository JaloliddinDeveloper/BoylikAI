using BoylikAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace BoylikAI.Integration.Tests.Infrastructure;

/// <summary>
/// Shared test fixture: spins up real PostgreSQL and Redis containers once per test collection.
/// All tests in a collection share the same containers for speed (Respawn resets data between tests).
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("boylikaitest")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private Respawner _respawner = null!;

    public string PostgresConnectionString { get; private set; } = string.Empty;
    public string RedisConnectionString { get; private set; } = string.Empty;
    public IServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Start containers in parallel
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        PostgresConnectionString = _postgres.GetConnectionString();
        RedisConnectionString = _redis.GetConnectionString();

        // Build minimal service provider for integration tests
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseNpgsql(PostgresConnectionString));

        // We need IPublisher — use a no-op for integration tests
        services.AddScoped<MediatR.IPublisher, NoOpPublisher>();

        Services = services.BuildServiceProvider();

        // Apply EF migrations to the test database
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        // Configure Respawn for fast DB reset between tests
        await using var conn = new Npgsql.NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"]
        });
    }

    /// <summary>Call at the start of each test to reset database state.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private sealed class NoOpPublisher : MediatR.IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : MediatR.INotification =>
            Task.CompletedTask;
    }
}

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>;
