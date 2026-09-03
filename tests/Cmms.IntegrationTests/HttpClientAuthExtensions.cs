using System.Net.Http.Json;

namespace Cmms.IntegrationTests;

/// <summary>
/// Drives the same cookie/CSRF login flow a real browser client would (see
/// src/Cmms.Api/AuthEndpoints.cs), so HTTP-level tests authenticate exactly the way the frontend
/// does rather than bypassing it.
/// </summary>
internal static class HttpClientAuthExtensions
{
    public static async Task<string> GetCsrfTokenAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/auth/csrf");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        return payload!.Token;
    }

    public static async Task LoginAsync(this HttpClient client, string email, string password)
    {
        var csrf = await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<HttpResponseMessage> PostJsonWithCsrfAsync<TBody>(
        this HttpClient client, string requestUri, TBody body)
    {
        var csrf = await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PutJsonWithCsrfAsync<TBody>(
        this HttpClient client, string requestUri, TBody body)
    {
        var csrf = await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PutBytesWithCsrfAsync(
        this HttpClient client, string requestUri, byte[] body, string contentType)
    {
        var csrf = await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private sealed record CsrfResponse(string Token);
}
