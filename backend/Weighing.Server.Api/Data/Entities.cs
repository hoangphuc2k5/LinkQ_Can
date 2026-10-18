namespace Weighing.Server.Api.Data;

public class WeighingRecordEntity
{
    public Guid Id { get; set; }
    public string LicensePlate { get; set; } = "";
    public decimal WeightKg { get; set; }
    public Direction Direction { get; set; }
    public CargoStatus CargoStatus { get; set; }
    public DateTime WeighedAt { get; set; }
    public string ClientId { get; set; } = "";
    public DateTime CreatedAtServer { get; set; }
    /// <summary>Số thứ tự tăng dần, cấp trong cùng transaction đã khóa SyncCounter => thứ tự commit == thứ tự sequence.</summary>
    public long ServerSequence { get; set; }
}

/// <summary>Một dòng duy nhất (Id=1) dùng làm "khóa tuần tự hóa" và bộ đếm sequence.</summary>
public class SyncCounter
{
    public const int SingletonId = 1;
    public int Id { get; set; }
    public long Value { get; set; }
}
