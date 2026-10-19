namespace Weighing.Local.Api;

public sealed class LocalOptions
{
    public const string Section = "Local";
    public string ClientId { get; set; } = "";                      // "Machine A" / "Machine B" - ổn định, đi kèm DB cục bộ
    public string ListenUrl { get; set; } = "http://127.0.0.1:5100"; // chỉ loopback
    /// <summary>Chỉ bật trong Docker image (bind 0.0.0.0 bên trong container; port được publish 127.0.0.1 trên host). Mặc định false.</summary>
    public bool AllowNonLoopback { get; set; } = false;
    public string DatabasePath { get; set; } = "weighing-local.db";
    public string ServerUrl { get; set; } = "";
    public string ServerApiKey { get; set; } = "";                   // đặt qua biến môi trường Local__ServerApiKey
    public int SyncIntervalSeconds { get; set; } = 10;
    public int PushBatchSize { get; set; } = 50;
    public int BackoffBaseSeconds { get; set; } = 2;
    public int BackoffMaxSeconds { get; set; } = 60;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public int HistoryTimeoutSeconds { get; set; } = 3;   // timeout khi đọc lịch sử từ server; quá thời gian thì dùng SQLite
    public string[] AllowedOrigins { get; set; } = ["http://localhost:4200"];
}
