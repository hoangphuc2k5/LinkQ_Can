namespace Weighing.Local.Api.Data;

public static class LocalBootstrap
{
    /// <summary>Migrate SQLite, tạo SyncState, và bảo đảm ClientId không bị đổi trên DB đã có dữ liệu.</summary>
    public static async Task InitializeAsync(IDbContextFactory<LocalDbContext> factory, string clientId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        var st = await db.SyncStates.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (st is null)
        {
            db.SyncStates.Add(new SyncState { Id = 1, ClientId = clientId, LastServerCursor = 0 });
            await db.SaveChangesAsync(ct);
        }
        else if (st.ClientId != clientId)
            throw new InvalidOperationException($"CSDL cục bộ thuộc về '{st.ClientId}', không khớp ClientId cấu hình '{clientId}'.");
    }
}
