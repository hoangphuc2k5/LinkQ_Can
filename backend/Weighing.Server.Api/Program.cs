using System.Text.Json.Serialization;
using Weighing.Server.Api.Auth;
using Weighing.Server.Api.Data;
using Weighing.Server.Api.Services;

namespace Weighing.Server.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(); // Chạy được cả console (dev/test) lẫn Windows Service (production).

        // Chuỗi kết nối lấy từ cấu hình/biến môi trường (ConnectionStrings__Default) - không hardcode mật khẩu.
        builder.Services.AddDbContext<ServerDbContext>((sp, o) =>
            o.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<SyncService>();
        builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();

        var apiKey = app.Configuration["Auth:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 16)
            throw new InvalidOperationException("Thiếu Auth:ApiKey (>= 16 ký tự). Đặt biến môi trường Auth__ApiKey.");

        if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ServerDbContext>().Database.MigrateAsync();
        }

        app.UseMiddleware<ApiKeyMiddleware>();
        app.MapControllers();
        await app.RunAsync();
    }
}
