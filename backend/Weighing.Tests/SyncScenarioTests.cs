using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Weighing.Local.Api.Data;
using Weighing.Local.Api.Sync;

namespace Weighing.Tests;

/// <summary>
/// Mô hình: mỗi trạm chỉ đẩy phiếu của chính mình lên server trung tâm; server lưu tất cả phiếu
/// và cung cấp tra cứu tập trung qua api/records. Trạm không kéo phiếu của máy khác về.
/// </summary>
public class SyncScenarioTests
{
    private static async Task<SyncResult> Sync(TestMachine m) => await m.Engine.SyncOnceAsync();

    private static void DeleteDb(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-shm", path + "-wal", path + "-journal" }) try { File.Delete(f); } catch { }
    }

    [Fact] // Test 1
    public async Task T1_MachineA_pushes_to_server_and_machineB_does_not_receive_it()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        await a.SaveAsync("51A-111.11", 12345.5m);
        var r = await Sync(a);
        Assert.Equal(SyncOutcome.Success, r.Outcome); Assert.Equal(1, r.Pushed); Assert.Equal(0, r.Pulled);

        var srv = Assert.Single(await server.RecordsAsync());
        Assert.Equal("51A-111.11", srv.LicensePlate);
        Assert.Equal("Machine A", srv.ClientId);
        Assert.Equal(12345.5m, srv.WeightKg);
        Assert.Equal(SyncStatus.Synced, Assert.Single(await a.LocalRecordsAsync()).SyncStatus);

        Assert.Equal(SyncOutcome.Success, (await Sync(b)).Outcome);
        Assert.Empty(await b.LocalRecordsAsync());
    }

    [Fact] // Test 2
    public async Task T2_MachineB_pushes_to_server_and_machineA_history_stays_local()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        await b.SaveAsync("29C-222.22", 800m, Direction.Out, CargoStatus.Empty);
        await Sync(b); await Sync(a);

        var srv = Assert.Single(await server.RecordsAsync());
        Assert.Equal("Machine B", srv.ClientId);
        Assert.Equal(Direction.Out, srv.Direction);
        Assert.Equal(CargoStatus.Empty, srv.CargoStatus);
        // Không sao chép dữ liệu về SQLite của máy A (tiết kiệm băng thông và dung lượng).
        Assert.Empty(await a.LocalRecordsAsync());
        // Nhưng lịch sử lấy từ server khi online thì máy A thấy phiếu của máy B.
        var history = await a.Service.QueryAsync(null, null, null, 1, 20);
        Assert.Equal("server", history.Source);
        Assert.Equal(1, history.Total);
        Assert.Equal("Machine B", Assert.Single(history.Items).ClientId);
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
        Assert.Equal(false, a.Tracker.Snapshot().Online);
    }

    [Fact] // Test 4
    public async Task T4_Reconnect_pushes_pending_to_server_only()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        a.Net.Offline = true;
        for (var i = 0; i < 5; i++) await a.SaveAsync($"51A-000.0{i}", 1000 + i);
        await Sync(a);
        a.Net.Offline = false;
        var r = await Sync(a);
        Assert.Equal(SyncOutcome.Success, r.Outcome); Assert.Equal(5, r.Pushed); Assert.Equal(0, r.Pulled);
        Assert.Equal(5, (await server.RecordsAsync()).Count);
        Assert.All(await a.LocalRecordsAsync(), x => Assert.Equal(SyncStatus.Synced, x.SyncStatus));
        await Sync(b);
        Assert.Empty(await b.LocalRecordsAsync());
    }

    [Fact] // Test 5
    public async Task T5_Both_offline_then_online_server_has_all_and_no_duplicates()
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
        Assert.Equal(3, (await a.LocalRecordsAsync()).Count);
        Assert.Equal(4, (await b.LocalRecordsAsync()).Count);
        Assert.All(await a.LocalRecordsAsync(), x => Assert.Equal(SyncStatus.Synced, x.SyncStatus));
        Assert.All(await b.LocalRecordsAsync(), x => Assert.Equal(SyncStatus.Synced, x.SyncStatus));
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
    public async Task T8_Restart_keeps_pending_data_and_pushes_after_restart()
    {
        using var server = new ServerFixture();
        var path = Path.Combine(Path.GetTempPath(), $"restart-{Guid.NewGuid():N}.db");
        try
        {
            using (var a1 = new TestMachine(server, "Machine A", path))
            {
                a1.Net.Offline = true;
                await a1.SaveAsync("R-1"); await a1.SaveAsync("R-2");
            } // "tắt ứng dụng"
            using (var a2 = new TestMachine(server, "Machine A", path))   // "mở lại" cùng file SQLite
            {
                Assert.Equal(2, (await a2.LocalRecordsAsync()).Count(r => r.SyncStatus == SyncStatus.Pending));
                var r = await Sync(a2);
                Assert.Equal(SyncOutcome.Success, r.Outcome); Assert.Equal(2, r.Pushed);
                Assert.Equal(2, (await server.RecordsAsync()).Count);
            }
            using (var a3 = new TestMachine(server, "Machine A", path))   // khởi động lại lần nữa: không còn Pending
            {
                Assert.Equal(0, (await a3.LocalRecordsAsync()).Count(r => r.SyncStatus == SyncStatus.Pending));
                var r = await Sync(a3);
                Assert.Equal(0, r.Pushed);
                Assert.Equal(2, (await server.RecordsAsync()).Count);
            }
        }
        finally { DeleteDb(path); }
    }

    [Fact] // Test 9
    public async Task T9_Concurrent_sync_no_loss_no_sequence_gap()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        for (var i = 0; i < 25; i++) { await a.SaveAsync($"A-{i}"); await b.SaveAsync($"B-{i}"); }
        await Task.WhenAll(Sync(a), Sync(b));
        await Task.WhenAll(Sync(a), Sync(b));
        await Sync(a); await Sync(b);
        var srv = await server.RecordsAsync();
        Assert.Equal(50, srv.Count);
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), srv.Select(x => x.ServerSequence).OrderBy(x => x)); // liên tục, không trùng, không hổng
        Assert.Equal(25, srv.Count(x => x.ClientId == "Machine A"));
        Assert.Equal(25, srv.Count(x => x.ClientId == "Machine B"));
        Assert.All(await a.LocalRecordsAsync(), x => Assert.Equal(SyncStatus.Synced, x.SyncStatus));
        Assert.All(await b.LocalRecordsAsync(), x => Assert.Equal(SyncStatus.Synced, x.SyncStatus));
        Assert.Equal(25, (await a.LocalRecordsAsync()).Count);
        Assert.Equal(25, (await b.LocalRecordsAsync()).Count);
    }

    [Fact]
    public async Task Records_api_lists_all_stations_with_client_filter_and_paging()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A"); using var b = new TestMachine(server, "Machine B");
        for (var i = 0; i < 5; i++) await a.SaveAsync($"A-{i}");
        await Sync(a);
        for (var i = 0; i < 3; i++) await b.SaveAsync($"B-{i}");
        await Sync(b);

        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ServerFixture.ApiKey);
        var all = await client.GetFromJsonAsync<ApiResponse<PagedResult<ServerWeighingRecordDto>>>(
            "/api/records?page=1&pageSize=2", SharedJson.Options);
        Assert.True(all!.Success);
        Assert.Equal(8, all.Data!.Total); Assert.Equal(2, all.Data.Items.Count);

        var onlyB = await client.GetFromJsonAsync<ApiResponse<PagedResult<ServerWeighingRecordDto>>>(
            "/api/records?clientId=Machine%20B", SharedJson.Options);
        Assert.Equal(3, onlyB!.Data!.Total);
        Assert.All(onlyB.Data.Items, x => Assert.Equal("Machine B", x.ClientId));

        using var anon = server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/records")).StatusCode);
    }

    [Fact]
    public async Task Local_history_shows_only_this_station_records()
    {
        using var server = new ServerFixture(); using var a = new TestMachine(server, "Machine A");
        await a.SaveAsync("51A-1.00");
        // Bản ghi có ClientId khác không được hiển thị trong lịch sử của trạm này.
        await using (var db = a.Factory.CreateDbContext())
        {
            db.WeighingRecords.Add(new LocalWeighingRecord
            {
                Id = Guid.NewGuid(), LicensePlate = "99B-9.99", WeightKg = 5m, Direction = Direction.In,
                CargoStatus = CargoStatus.Loaded, WeighedAt = DateTime.UtcNow, ClientId = "Machine B", SyncStatus = SyncStatus.Synced
            });
            await db.SaveChangesAsync();
        }
        // Server không khả dụng => fallback SQLite: chỉ phiếu của chính trạm này.
        a.Net.Offline = true;
        var page = await a.Service.QueryAsync(null, null, null, 1, 20);
        Assert.Equal("local", page.Source);
        Assert.NotNull(page.Warning);
        Assert.Equal(1, page.Total);
        var item = Assert.Single(page.Items);
        Assert.Equal("Machine A", item.ClientId);
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/sync/push", null)).StatusCode);
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

    [Fact]
    public async Task Wrong_client_id_on_existing_database_is_rejected()
    {
        using var server = new ServerFixture();
        using var a = new TestMachine(server, "Machine A");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Weighing.Local.Api.Data.LocalBootstrap.InitializeAsync(a.Factory, "Machine B"));
    }
}
