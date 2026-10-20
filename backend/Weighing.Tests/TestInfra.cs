using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Weighing.Local.Api;
using Weighing.Local.Api.Data;
using Weighing.Local.Api.Services;
using Weighing.Local.Api.Sync;
using Weighing.Server.Api.Data;

namespace Weighing.Tests;

/// <summary>Central API thật (controllers, middleware, SyncService, migration) chạy in-process; chỉ thay provider DB bằng SQLite file cho test.</summary>
public sealed class ServerFixture : WebApplicationFactory<Weighing.Server.Api.Program>
{
    public const string ApiKey = "test-api-key-1234567890";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"srv-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Testing");
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:ApiKey"] = ApiKey, ["Database:AutoMigrate"] = "true", ["ConnectionStrings:Default"] = "Server=unused"
        }));
        b.ConfigureServices(s =>
        {
            foreach (var d in s.Where(x => x.ServiceType == typeof(DbContextOptions<ServerDbContext>)).ToList()) s.Remove(d);
            s.AddDbContext<ServerDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        });
    }

    public async Task<List<WeighingRecordEntity>> RecordsAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        return await db.WeighingRecords.AsNoTracking().OrderBy(r => r.ServerSequence).ToListAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal", _dbPath + "-journal" })
            try { File.Delete(f); } catch { /* best effort */ }
    }
}

/// <summary>Giả lập mạng: Offline (ném HttpRequestException), hoặc server xử lý xong nhưng response bị mất.</summary>
public sealed class FlakyHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public volatile bool Offline;
    public int DropNextPushResponses;
    public Func<Task>? AfterNextPush;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (Offline) throw new HttpRequestException("simulated offline");
        var resp = await base.SendAsync(req, ct);
        if (req.RequestUri!.AbsolutePath.EndsWith("/sync/push") && AfterNextPush is { } afterPush)
        {
            AfterNextPush = null;
            await afterPush();
        }
        if (req.RequestUri!.AbsolutePath.EndsWith("/sync/push") && DropNextPushResponses > 0)
        {
            Interlocked.Decrement(ref DropNextPushResponses);
            resp.Dispose();
            throw new HttpRequestException("simulated lost response (server already committed)");
        }
        return resp;
    }
}

public sealed class TestFactory(string path) : IDbContextFactory<LocalDbContext>
{
    public LocalDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LocalDbContext>().UseSqlite($"Data Source={path}").Options);
}

/// <summary>Một "máy" client: SQLite file riêng + SyncEngine + WeighingService thật, nói chuyện với Central API qua handler giả lập mạng.</summary>
public sealed class TestMachine : IDisposable
{
    public string ClientId { get; }
    public string DbPath { get; }
    public FlakyHandler Net { get; }
    public ServerApiClient Api { get; }
    public IDbContextFactory<LocalDbContext> Factory { get; }
    public SyncEngine Engine { get; }
    public WeighingService Service { get; }
    public SyncStatusTracker Tracker { get; } = new();
    private readonly bool _ownsFile;

    public TestMachine(ServerFixture server, string clientId, string? dbPath = null)
    {
        ClientId = clientId;
        _ownsFile = dbPath is null;
        DbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"loc-{Guid.NewGuid():N}.db");
        var opt = new LocalOptions { ClientId = clientId, DatabasePath = DbPath, PushBatchSize = 50, HistoryTimeoutSeconds = 3 };
        var opts = Microsoft.Extensions.Options.Options.Create(opt);
        Factory = new TestFactory(DbPath);
        LocalBootstrap.InitializeAsync(Factory, clientId).GetAwaiter().GetResult();

        Net = new FlakyHandler(server.Server.CreateHandler());
        var http = new HttpClient(Net) { BaseAddress = server.Server.BaseAddress };
        http.DefaultRequestHeaders.Add("X-Api-Key", ServerFixture.ApiKey);
        Api = new ServerApiClient(http);
        Engine = new SyncEngine(Factory, Api, Tracker, opts, TimeProvider.System, NullLogger<SyncEngine>.Instance);
        Service = new WeighingService(Factory, opts, TimeProvider.System, new SyncTrigger(), Api);
    }

    public Task<LocalWeighingDto> SaveAsync(string plate = "51A-123.45", decimal kg = 1000m,
        Direction dir = Direction.In, CargoStatus cargo = CargoStatus.Loaded) =>
        Service.CreateAsync(new CreateWeighingRequest { LicensePlate = plate, FirstWeightKg = kg, Direction = dir, CargoStatus = cargo });

    public async Task<List<LocalWeighingRecord>> LocalRecordsAsync()
    {
        await using var db = Factory.CreateDbContext();
        return await db.WeighingRecords.AsNoTracking().ToListAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (!_ownsFile) return; // file do test sở hữu (vd. kịch bản khởi động lại) tự dọn
        foreach (var f in new[] { DbPath, DbPath + "-shm", DbPath + "-wal", DbPath + "-journal" })
            try { File.Delete(f); } catch { }
    }
}
