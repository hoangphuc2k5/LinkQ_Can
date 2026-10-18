namespace Weighing.Local.Api.Data;

public class LocalWeighingRecord
{
    public Guid Id { get; set; }
    public string LicensePlate { get; set; } = "";
    public decimal WeightKg { get; set; }
    public Direction Direction { get; set; }
    public CargoStatus CargoStatus { get; set; }
    public DateTime WeighedAt { get; set; }
    public string ClientId { get; set; } = "";
    public SyncStatus SyncStatus { get; set; }       // Pending = đồng thời là "outbox"
    public DateTime? SyncedAt { get; set; }
    public string? LastSyncError { get; set; }
    public int RetryCount { get; set; }
    public long? ServerSequence { get; set; }

    public WeighingRecordDto ToDto() => new(Id, LicensePlate, WeightKg, Direction, CargoStatus, WeighedAt, ClientId);
    public LocalWeighingDto ToLocalDto() => new(Id, LicensePlate, WeightKg, Direction, CargoStatus, WeighedAt, ClientId,
        SyncStatus, SyncedAt, LastSyncError, RetryCount);
}

public class SyncState
{
    public int Id { get; set; } = 1;
    public string ClientId { get; set; } = "";
    public long LastServerCursor { get; set; }
    public DateTime? LastSuccessfulSyncAt { get; set; }
}
