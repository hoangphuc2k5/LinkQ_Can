using System.Security.Cryptography;
using System.Text;

namespace Weighing.Server.Api.Auth;

/// <summary>Xác thực bằng API key (header X-Api-Key) cho mọi route /api/sync. /api/health để ẩn danh.</summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration cfg, ILogger<ApiKeyMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api/sync"))
        {
            var expected = Encoding.UTF8.GetBytes(cfg["Auth:ApiKey"] ?? "");
            var provided = Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Api-Key"].ToString());
            if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(expected, provided))
            {
                log.LogWarning("Unauthorized sync request from {RemoteIp}", ctx.Connection.RemoteIpAddress);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(ApiResponse<object>.Failed("unauthorized", "API key không hợp lệ."), SharedJson.Options);
                return;
            }
        }
        await next(ctx);
    }
}
