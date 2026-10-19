using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weighing.Shared;

namespace Weighing.Server.Api.Auth;

/// <summary>Xác thực bằng API key (header X-Api-Key) cho /api/sync (trạm đẩy phiếu) và /api/records (tra cứu). /api/health để ẩn danh.</summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration cfg, ILogger<ApiKeyMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api/sync") || ctx.Request.Path.StartsWithSegments("/api/records"))
        {
            var expected = Encoding.UTF8.GetBytes(cfg["Auth:ApiKey"] ?? "");
            var provided = Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Api-Key"].ToString());
            if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(expected, provided))
            {
                log.LogWarning("Unauthorized sync request from {RemoteIp}", ctx.Connection.RemoteIpAddress);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                var body = JsonSerializer.SerializeToUtf8Bytes(ApiResponse<object>.Failed("unauthorized", "API key không hợp lệ."), SharedJson.Options);
                await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted);
                return;
            }
        }
        await next(ctx);
    }
}
