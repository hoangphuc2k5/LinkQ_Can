using System.Net;
using Microsoft.Data.Sqlite;
using Weighing.Local.Api.Sync;

namespace Weighing.Tests;

/// <summary>Các kịch bản bắt buộc Test 1..9 (mục 12 của đề bài) + xác thực, xung đột, phân trang.</summary>
public class SyncScenarioTests
{
    private static async Task<SyncResult> Sync(TestMachine m) => await m.Engine.SyncOnceAsync();

    [Fact] // Test 1
    public async Task T1_MachineA_to_MachineB()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        await a.SaveAsync("51A-111.11", 12345.5m);
        Assert.Equal(SyncOutcome.Success, (await Sync(a)).Outcome);
        Assert.Equal(SyncOutcome.Success, (await Sync(b)).Outcome);
        var onB = Assert.Single(await b.LocalRecordsAsync());
        Assert.Equal("51A-111.11", onB.LicensePlate);
        Assert.Equal("Machine A", onB.ClientId);
        Assert.Equal(SyncStatus.Synced, onB.SyncStatus);
        Assert.Equal(12345.5m, onB.WeightKg);
    }

    [Fact] // Test 2
    public async Task T2_MachineB_to_MachineA()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        await b.SaveAsync("29C-222.22", 800m, Direction.Out, CargoStatus.Empty);
        await Sync(b); await Sync(a);
        var onA = Assert.Single(await a.LocalRecordsAsync());
        Assert.Equal("Machine B", onA.ClientId);
        Assert.Equal(Direction.Out, onA.Direction);
        Assert.Equal(CargoStatus.Empty, onA.CargoStatus);
    }

    [Fact] // Test 3
    public async Task T3_Offline_saves_5_records_locally()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A");
        a.Net.Offline = true;
        for (var i = 0; i < 5; i++) await a.SaveAsync($"51A-000.0{i}", 1000 + i);
        Assert.Equal(SyncOutcome.Offline, (await Sync(a)).Outcome);
        var local = await a.LocalRecordsAsync();
        Assert.Equal(5, local.Count);
        Assert.All(local, r => Assert.Equal(SyncStatus.Pending, r.SyncStatus));
        Assert.Empty(await server.RecordsAsync());
        Assert.False(a.Tracker.Snapshot().Online);
    }

    [Fact] // Test 4
    public async Task T4_Reconnect_syncs_to_server_and_machineB()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        a.Net.Offline = true;
        for (var i = 0; i < 5; i++) await a.SaveAsync($"51A-000.0{i}", 1000 + i);
        await Sync(a);
        a.Net.Offline = false;
        var r = await Sync(a);
        Assert.Equal(SyncOutcome.Success, r.Outcome); Assert.Equal(5, r.Pushed);
        Assert.Equal(5, (await server.RecordsAsync()).Count);
        Assert.All(await a.LocalRecordsAsync(), x => Assert.Equal(SyncStatus.Synced, x.SyncStatus));
        await Sync(b);
        Assert.Equal(5, (await b.LocalRecordsAsync()).Count);
    }

    [Fact] // Test 5
    public async Task T5_Both_offline_then_online_no_duplicates()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        a.Net.Offline = b.Net.Offline = true;
        for (var i = 0; i < 3; i++) await a.SaveAsync($"A-{i}");
        for (var i = 0; i < 4; i++) await b.SaveAsync($"B-{i}");
        a.Net.Offline = b.Net.Offline = false;
        await Sync(a); await Sync(b); await Sync(a);
        var srv = await server.RecordsAsync();
        Assert.Equal(7, srv.Count);
        Assert.Equal(7, srv.Select(x => x.Id).Distinct().Count());
        Assert.Equal(7, (await a.LocalRecordsAsync()).Count);
        Assert.Equal(7, (await b.LocalRecordsAsync()).Count);
    }

    [Fact] // Test 6
    public async Task T6_Duplicate_push_stores_one_record()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A");
        var dto = new WeighingRecordDto(Guid.NewGuid(), "51A-999.99", 5000m, Direction.In, CargoStatus.Loaded, DateTime.UtcNow, "Machine A");
        var req = new PushRequest("Machine A", [dto]);
        var first = await a.Api.PushAsync(req, default);
        var second = await a.Api.PushAsync(req, default);
        var third = await a.Api.PushAsync(req, default);
        Assert.Equal(PushItemStatus.Accepted, first.Results[0].Status);
        Assert.Equal(PushItemStatus.Duplicate, second.Results[0].Status);
        Assert.Equal(PushItemStatus.Duplicate, third.Results[0].Status);
        Assert.Equal(first.Results[0].ServerSequence, third.Results[0].ServerSequence);
        Assert.Single(await server.RecordsAsync());
    }

    [Fact]
    public async Task T6b_Same_id_different_content_is_conflict_and_not_overwritten()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A");
        var id = Guid.NewGuid(); var t = DateTime.UtcNow;
        await a.Api.PushAsync(new("Machine A", [new(id, "51A-1", 1000m, Direction.In, CargoStatus.Loaded, t, "Machine A")]), default);
        var res = await a.Api.PushAsync(new("Machine A", [new(id, "51A-1", 9999m, Direction.In, CargoStatus.Loaded, t, "Machine A")]), default);
        Assert.Equal(PushItemStatus.Conflict, res.Results[0].Status);
        Assert.Equal(1000m, Assert.Single(await server.RecordsAsync()).WeightKg);
    }

    [Fact] // Test 7
    public async Task T7_Server_commits_but_response_lost_retry_creates_no_duplicate()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A");
        await a.SaveAsync("51A-777.77", 7000m);
        a.Net.DropNextPushResponses = 1;
        var first = await Sync(a);
        Assert.NotEqual(SyncOutcome.Success, first.Outcome);
        Assert.Single(await server.RecordsAsync());                       // server đã commit
        var local = Assert.Single(await a.LocalRecordsAsync());
        Assert.Equal(SyncStatus.Pending, local.SyncStatus);                // client chưa biết => vẫn Pending
        Assert.Equal(1, local.RetryCount);
        Assert.Equal(SyncOutcome.Success, (await Sync(a)).Outcome);        // retry an toàn
        Assert.Single(await server.RecordsAsync());
        Assert.Equal(SyncStatus.Synced, Assert.Single(await a.LocalRecordsAsync()).SyncStatus);
    }

    [Fact] // Test 8
    public async Task T8_Restart_keeps_pending_data_and_cursor()
    {
        using var server = new ServerFixture();
        var path = Path.Combine(Path.GetTempPath(), $"restart-{Guid.NewGuid():N}.db");
        using (var a1 = new TestMachine(server, "Machine A", path))
        {
            a1.Net.Offline = true;
            await a1.SaveAsync("R-1"); await a1.SaveAsync("R-2");
        } // "tắt ứng dụng"
        using var a2 = new TestMachine(server, "Machine A", path);   // "mở lại" cùng file SQLite
        Assert.Equal(2, (await a2.LocalRecordsAsync()).Count(r => r.SyncStatus == SyncStatus.Pending));
        Assert.Equal(SyncOutcome.Success, (await Sync(a2)).Outcome);
        Assert.Equal(2, (await server.RecordsAsync()).Count);
        var cursor = await a2.CursorAsync();
        Assert.Equal(2, cursor);
        using var a3 = new TestMachine(server, "Machine A", path);   // khởi động lại lần nữa: cursor còn nguyên
        Assert.Equal(cursor, await a3.CursorAsync());
        var r = await Sync(a3);
        Assert.Equal(0, r.Pulled); Assert.Equal(0, r.Pushed);
        a3.Dispose(); SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-shm", path + "-wal", path + "-journal" }) try { File.Delete(f); } catch { }
    }

    [Fact]
    public async Task T8b_Wrong_client_id_on_existing_database_is_rejected()
    {
        using var server = new ServerFixture();
        using var a = new TestMachine(server, "Machine A");
        await Assert.ThrowsAsync<InvalidOperationException>(() => LocalBootstrap_Init(a.Factory, "Machine B"));
    }
    private static Task LocalBootstrap_Init(IDbContextFactory<Weighing.Local.Api.Data.LocalDbContext> f, string id) =>
        Weighing.Local.Api.Data.LocalBootstrap.InitializeAsync(f, id);

    [Fact] // Test 9
    public async Task T9_Concurrent_sync_no_loss_no_cursor_gap()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        for (var i = 0; i < 25; i++) { await a.SaveAsync($"A-{i}"); await b.SaveAsync($"B-{i}"); }
        await Task.WhenAll(Sync(a), Sync(b));
        await Task.WhenAll(Sync(a), Sync(b));
        await Sync(a); await Sync(b);
        var srv = await server.RecordsAsync();
        Assert.Equal(50, srv.Count);
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), srv.Select(x => x.ServerSequence)); // liên tục, không trùng, không hổng
        Assert.Equal(50, (await a.LocalRecordsAsync()).Count);
        Assert.Equal(50, (await b.LocalRecordsAsync()).Count);
        Assert.Equal(50, await a.CursorAsync()); Assert.Equal(50, await b.CursorAsync());
    }

    [Fact]
    public async Task Pull_is_paged_and_cursor_advances_per_page()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B", pullPage: 2);
        for (var i = 0; i < 5; i++) await a.SaveAsync($"P-{i}");
        await Sync(a);
        var r = await Sync(b);
        Assert.Equal(5, r.Pulled);
        Assert.Equal(5, await b.CursorAsync());
        var page = await a.Api.PullAsync(0, 2, default);
        Assert.True(page.HasMore); Assert.Equal(2, page.Records.Count); Assert.Equal(2, page.NextCursor);
    }

    [Fact]
    public async Task Invalid_record_is_rejected_by_server()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A");
        var bad = new WeighingRecordDto(Guid.NewGuid(), "51A-1", 0m, Direction.In, CargoStatus.Loaded, DateTime.UtcNow, "Machine A");
        var res = await a.Api.PushAsync(new("Machine A", [bad]), default);
        Assert.Equal(PushItemStatus.Invalid, res.Results[0].Status);
        Assert.Empty(await server.RecordsAsync());
    }

    [Fact]
    public async Task Sync_endpoints_require_api_key_but_health_is_open()
    {
        using var server = new ServerFixture();
        using var client = server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sync/pull?cursor=0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Local_service_rejects_invalid_weight_and_normalizes_plate()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A");
        var saved = await a.SaveAsync("  51a-123.45  ", 10m);
        Assert.Equal("51A-123.45", saved.LicensePlate);
        await Assert.ThrowsAsync<Weighing.Local.Api.Services.WeighingValidationException>(() => a.SaveAsync("51A-1", 0m));
    }
}
