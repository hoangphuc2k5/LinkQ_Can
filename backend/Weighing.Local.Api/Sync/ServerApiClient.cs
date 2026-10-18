using System.Net;
using System.Net.Http.Json;

namespace Weighing.Local.Api.Sync;

public interface IServerApiClient
{
    Task<bool> IsHealthyAsync(CancellationToken ct);
    Task<PushResponse> PushAsync(PushRequest request, CancellationToken ct);
    Task<PullResponse> PullAsync(long cursor, int limit, CancellationToken ct);
}

public sealed class ServerApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

public sealed class ServerApiClient(HttpClient http) : IServerApiClient
{
    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync("api/health", ct);
            return r.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public async Task<PushResponse> PushAsync(PushRequest request, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("api/sync/push", request, SharedJson.Options, ct);
        return await ReadAsync<PushResponse>(r, ct);
    }

    public async Task<PullResponse> PullAsync(long cursor, int limit, CancellationToken ct)
    {
        using var r = await http.GetAsync($"api/sync/pull?cursor={cursor}&limit={limit}", ct);
        return await ReadAsync<PullResponse>(r, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r, CancellationToken ct)
    {
        if (!r.IsSuccessStatusCode)
        {
            var body = await r.Content.ReadAsStringAsync(ct);
            throw new ServerApiException(r.StatusCode, $"Server trả về {(int)r.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
        }
        var api = await r.Content.ReadFromJsonAsync<ApiResponse<T>>(SharedJson.Options, ct);
        if (api is null || !api.Success || api.Data is null)
            throw new ServerApiException(r.StatusCode, "Phản hồi từ server không hợp lệ.");
        return api.Data;
    }
}
