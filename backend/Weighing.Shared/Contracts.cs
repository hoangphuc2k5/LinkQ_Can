using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Weighing.Shared;

public enum Direction { In = 1, Out = 2 }
public enum CargoStatus { Loaded = 1, Empty = 2 }
public enum SyncStatus { Pending = 0, Synced = 1, Error = 2 }
public enum PushItemStatus { Accepted = 1, Duplicate = 2, Conflict = 3, Invalid = 4 }

// ----- Sync contract (Local <-> Central) -----
public sealed record WeighingRecordDto(Guid Id, string LicensePlate, decimal FirstWeightKg, decimal? SecondWeightKg,
    decimal? CargoWeightKg, Direction Direction, CargoStatus CargoStatus, DateTime WeighedAt, string ClientId);

public sealed record ServerWeighingRecordDto(Guid Id, string LicensePlate, decimal FirstWeightKg, decimal? SecondWeightKg,
    decimal? CargoWeightKg, Direction Direction, CargoStatus CargoStatus, DateTime WeighedAt, string ClientId, long ServerSequence);

public sealed record PushRequest(string ClientId, List<WeighingRecordDto> Records);
public sealed record PushItemResult(Guid Id, PushItemStatus Status, long? ServerSequence, string? Message);
public sealed record PushResponse(List<PushItemResult> Results);

// ----- Unified response -----
public sealed record ApiError(string Code, string Message);
public sealed record ApiResponse<T>(bool Success, T? Data, ApiError? Error)
{
    public static ApiResponse<T> Succeeded(T data) => new(true, data, null);
    public static ApiResponse<T> Failed(string code, string message) => new(false, default, new ApiError(code, message));
}

// ----- Local API DTOs -----
public sealed class CreateWeighingRequest
{
    [Required, StringLength(20, MinimumLength = 1)] public string LicensePlate { get; init; } = "";
    [Range(typeof(decimal), "0.001", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Cân lần 1 phải lớn hơn 0 và tối đa 1.000.000 kg")]
    public decimal FirstWeightKg { get; init; }
    [EnumDataType(typeof(Direction))] public Direction Direction { get; init; }
    [EnumDataType(typeof(CargoStatus))] public CargoStatus CargoStatus { get; init; }
}

public sealed record LocalWeighingDto(Guid Id, string LicensePlate, decimal FirstWeightKg, decimal? SecondWeightKg,
    decimal? CargoWeightKg, Direction Direction, CargoStatus CargoStatus, DateTime WeighedAt, string ClientId,
    SyncStatus SyncStatus, DateTime? SyncedAt, string? LastSyncError, int RetryCount);

/// <summary>Nhập cân lần 2 (xe sau khi dỡ hàng). Trọng lượng hàng do server tự tính.</summary>
public sealed class SecondWeighRequest
{
    [Range(typeof(decimal), "0.001", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Cân lần 2 phải lớn hơn 0 và tối đa 1.000.000 kg")]
    public decimal SecondWeightKg { get; init; }
}

public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

/// <summary>Kết quả lịch sử của Local: lấy từ server khi online, hoặc từ SQLite cục bộ khi offline.</summary>
public sealed record HistoryResult(List<LocalWeighingDto> Items, int Total, int Page, int PageSize,
    string Source, string? Warning);

public sealed record SyncStatusDto(string ClientId, bool? ServerOnline, bool IsSyncing, int PendingCount, int ErrorCount,
    DateTime? LastSuccessfulSyncAt, string? LastError, DateTime? NextRetryAt, int ConsecutiveFailures);

public sealed record SyncNowResultDto(string Outcome, string? Error, int Pushed, int Pulled);

// ----- Helpers -----
public static class SharedJson
{
    public static readonly JsonSerializerOptions Options = Create();
    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}

public static class PlateNormalizer
{
    public static string Normalize(string? plate) =>
        string.Join(' ', (plate ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}

public static class WeighingValidator
{
    /// <summary>Trả về null nếu hợp lệ, ngược lại là thông báo lỗi.</summary>
    public static string? Validate(WeighingRecordDto r, DateTime utcNow)
    {
        if (r.Id == Guid.Empty) return "Id không hợp lệ.";
        if (string.IsNullOrWhiteSpace(r.LicensePlate) || r.LicensePlate.Length > 20) return "Biển số bắt buộc, tối đa 20 ký tự.";
        if (r.LicensePlate != PlateNormalizer.Normalize(r.LicensePlate)) return "Biển số phải được chuẩn hóa (viết hoa, bỏ khoảng trắng thừa).";
        if (r.FirstWeightKg <= 0 || r.FirstWeightKg > 1_000_000m) return "Cân lần 1 phải > 0 và <= 1.000.000 kg.";
        if (decimal.Round(r.FirstWeightKg, 3) != r.FirstWeightKg) return "Cân lần 1 tối đa 3 chữ số thập phân.";
        if (!Enum.IsDefined(r.Direction)) return "Loại lượt cân không hợp lệ.";
        if (!Enum.IsDefined(r.CargoStatus)) return "Tình trạng hàng không hợp lệ.";
        if (r.WeighedAt.ToUniversalTime() > utcNow.AddDays(1)) return "Thời gian cân nằm ở tương lai.";
        if (string.IsNullOrWhiteSpace(r.ClientId) || r.ClientId.Length > 50) return "ClientId không hợp lệ.";
        return ValidateSecondWeigh(r.FirstWeightKg, r.SecondWeightKg, r.CargoWeightKg);
    }

    /// <summary>Cân lần 2 chưa có thì trọng lượng hàng cũng phải trống. Có cân lần 2 thì hàng = lần 1 − lần 2.</summary>
    public static string? ValidateSecondWeigh(decimal firstWeightKg, decimal? secondWeightKg, decimal? cargoWeightKg)
    {
        if (secondWeightKg is null && cargoWeightKg is null) return null;
        if (secondWeightKg is null || cargoWeightKg is null) return "Cân lần 2 và trọng lượng hàng phải đi cùng nhau.";
        if (secondWeightKg <= 0 || secondWeightKg > 1_000_000m) return "Cân lần 2 phải > 0 và <= 1.000.000 kg.";
        if (decimal.Round(secondWeightKg.Value, 3) != secondWeightKg) return "Cân lần 2 tối đa 3 chữ số thập phân.";
        if (secondWeightKg >= firstWeightKg) return "Cân lần 2 phải nhỏ hơn cân lần 1.";
        if (cargoWeightKg != firstWeightKg - secondWeightKg) return "Trọng lượng hàng phải bằng cân lần 1 trừ cân lần 2.";
        return null;
    }
}

/// <summary>Luôn đọc DateTime từ DB với Kind=Utc (SQLite/SQL Server không lưu Kind).</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
}
public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
}
