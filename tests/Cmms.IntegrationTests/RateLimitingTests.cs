using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// docs/02-security-and-invariants.md's threat-model row: "Rate-limit login, request-submission,
/// uploads, and exports" (src/Cmms.Api/Program.cs's global limiter + the "auth" policy on
/// POST /auth/login in AuthEndpoints.cs). Every other test class in this suite runs with the limits
/// raised to effectively-unlimited (see CmmsWebApplicationFactory) so unrelated assertions never
/// trip on 429s that have nothing to do with what they're testing — this is the one place that
/// actually exercises the real, low, production-shaped limit.
///
/// Relies on this suite's tests all sharing one xUnit collection ("Postgres"), which xUnit runs
/// sequentially — the process-wide environment-variable override below would be a race condition
/// otherwise.
/// </summary>
[Collection("Postgres")]
public sealed class RateLimitingTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;

    public RateLimitingTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new CmmsWebApplicationFactory(_postgres.ConnectionString);
        // Overrides the 100000 the factory's own constructor just set — read once, lazily, when
        // the host actually builds on the first CreateClient() call below, not at this point.
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", "3");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", "100000");
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Repeated_login_attempts_from_the_same_client_are_eventually_rejected_with_429()
    {
        using var client = _factory.CreateClient();

        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var csrf = await client.GetCsrfTokenAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(new { email = "nobody@example.test", password = "wrong-password" })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            using var response = await client.SendAsync(request);
            statusCodes.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
        // The limit is 3 permits — the first few attempts should still be evaluated normally
        // (Unauthorized for a bad password), not immediately rate-limited.
        Assert.Contains(HttpStatusCode.Unauthorized, statusCodes);
    }
}
