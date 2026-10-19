using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Weighing.Local.Api.Data;
using Weighing.Local.Api.Services;
using Weighing.Local.Api.Sync;

namespace Weighing.Local.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(); // Chạy được cả console (dev/test) lẫn Windows Service (production).
        builder.Services.Configure<LocalOptions>(builder.Configuration.GetSection(LocalOptions.Section));
        var listenUrl = builder.Configuration[$"{LocalOptions.Section}:ListenUrl"] ?? "http://127.0.0.1:5100";
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var u) || !(u.IsLoopback))
            throw new InvalidOperationException("Local:ListenUrl phải là địa chỉ loopback (127.0.0.1/localhost) để không lộ ra mạng LAN.");
        builder.WebHost.UseUrls(listenUrl);

        builder.Services.AddDbContextFactory<LocalDbContext>((sp, o) =>
            o.UseSqlite($"Data Source={sp.GetRequiredService<IOptions<LocalOptions>>().Value.DatabasePath}"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SyncStatusTracker>();
        builder.Services.AddSingleton<SyncTrigger>();
        builder.Services.AddHttpClient<IServerApiClient, ServerApiClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<LocalOptions>>().Value;
            c.BaseAddress = new Uri(o.ServerUrl.TrimEnd('/') + "/");
            c.Timeout = TimeSpan.FromSeconds(o.HttpTimeoutSeconds);
            c.DefaultRequestHeaders.Add("X-Api-Key", o.ServerApiKey);
        });
        builder.Services.AddSingleton<SyncEngine>();
        builder.Services.AddSingleton<WeighingService>();
        builder.Services.AddHostedService<SyncBackgroundService>();

        var origins = builder.Configuration.GetSection($"{LocalOptions.Section}:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:4200" };
        builder.Services.AddCors(o => o.AddPolicy("ui", p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
        builder.Services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
                new BadRequestObjectResult(ApiResponse<object>.Failed("validation",
                    string.Join("; ", ctx.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)))));

        var app = builder.Build();

        var opt = app.Services.GetRequiredService<IOptions<LocalOptions>>().Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId)) throw new InvalidOperationException("Thiếu Local:ClientId (vd. 'Machine A').");
        if (string.IsNullOrWhiteSpace(opt.ServerUrl)) throw new InvalidOperationException("Thiếu Local:ServerUrl.");
        if (string.IsNullOrWhiteSpace(opt.ServerApiKey)) throw new InvalidOperationException("Thiếu Local:ServerApiKey (biến môi trường Local__ServerApiKey).");
        await LocalBootstrap.InitializeAsync(app.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>(), opt.ClientId);

        app.UseCors("ui");
        app.MapControllers();
        await app.RunAsync();
    }
}
