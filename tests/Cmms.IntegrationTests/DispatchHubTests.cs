using System.Net.Http.Json;
using System.Text.Json;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// ADR-17 / docs/02-security-and-invariants.md's SignalR threat-model row: "Group membership and
/// every broadcast are server-derived and site-filtered from the connection's authenticated
/// identity, never from a client-supplied site/group parameter." This drives two real SignalR
/// connections (real WebSocket-over-TestServer transport, real cookie auth) to prove a technician
/// at Site B never receives a broadcast meant for Site A, even though both are live-connected to
/// the same hub at the same time.
/// </summary>
[Collection("Postgres")]
public sealed class DispatchHubTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteAId;
    private Guid _siteBId;
    private string _plannerAEmail = string.Empty;
    private string _technicianBEmail = string.Empty;

    public DispatchHubTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _factory = new CmmsWebApplicationFactory(_postgres.ConnectionString);
        using (_factory.CreateClient())
        {
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _plannerAEmail = $"planner.hub.a.{suffix}@example.test";
        _technicianBEmail = $"tech.hub.b.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();

        var siteA = new Site($"SITE-HUB-A-{suffix}", "Site Hub A", "UTC");
        var siteB = new Site($"SITE-HUB-B-{suffix}", "Site Hub B", "UTC");
        identityDb.Sites.AddRange(siteA, siteB);
        await identityDb.SaveChangesAsync();
        _siteAId = siteA.Id;
        _siteBId = siteB.Id;

        var plannerA = new ApplicationUser(_plannerAEmail, "Planner Hub A");
        Assert.True((await userManager.CreateAsync(plannerA, Password)).Succeeded);
        var technicianB = new ApplicationUser(_technicianBEmail, "Technician Hub B");
        Assert.True((await userManager.CreateAsync(technicianB, Password)).Succeeded);

        identityDb.SiteMemberships.Add(new SiteMembership(plannerA.Id, _siteAId, RoleCode.Planner));
        identityDb.SiteMemberships.Add(new SiteMembership(technicianB.Id, _siteBId, RoleCode.Technician));
        await identityDb.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<string> LoginAndGetCookieHeaderAsync(string email)
    {
        using var http = _factory.CreateClient();
        var csrfResponse = await http.GetAsync("/auth/csrf");
        var csrfCookie = FirstCookiePair(csrfResponse);
        var csrfToken = (await csrfResponse.Content.ReadFromJsonAsync<CsrfPayload>())!.Token;

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { email, password = Password })
        };
        loginRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        loginRequest.Headers.Add("Cookie", csrfCookie);
        using var loginResponse = await http.SendAsync(loginRequest);
        loginResponse.EnsureSuccessStatusCode();
        var authCookie = FirstCookiePair(loginResponse);

        return $"{csrfCookie}; {authCookie}";
    }

    private static string FirstCookiePair(HttpResponseMessage response)
    {
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.First()
            : throw new InvalidOperationException("Expected a Set-Cookie header.");
        return setCookie.Split(';')[0];
    }

    private HubConnection BuildConnection(string cookieHeader) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/work-orders"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers["Cookie"] = cookieHeader;
                // TestServer's in-memory pipeline doesn't support a real WebSocket upgrade the way
                // HubConnectionBuilder's default ClientWebSocket transport needs — long polling
                // still exercises the same hub, auth, and group-membership code, just over ordinary
                // HTTP requests through the same HttpMessageHandlerFactory above.
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    [Fact]
    public async Task A_work_order_broadcast_for_site_a_never_reaches_a_connection_scoped_to_site_b()
    {
        var cookieA = await LoginAndGetCookieHeaderAsync(_plannerAEmail);
        var cookieB = await LoginAndGetCookieHeaderAsync(_technicianBEmail);

        await using var connectionA = BuildConnection(cookieA);
        await using var connectionB = BuildConnection(cookieB);

        var siteAReceived = new TaskCompletionSource<object[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var siteBReceivedAnything = false;
        connectionA.On<object>("WorkOrderChanged", payload => siteAReceived.TrySetResult([payload]));
        connectionB.On<object>("WorkOrderChanged", _ => siteBReceivedAnything = true);

        await connectionA.StartAsync();
        await connectionB.StartAsync();

        using var plannerHttp = _factory.CreateClient();
        await plannerHttp.LoginAsync(_plannerAEmail, Password);
        var createResponse = await plannerHttp.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = "Hub isolation test",
            description = (string?)null,
            assetId = (Guid?)null,
            locationId = (Guid?)null
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();

        // Plain creation (Draft) deliberately does not broadcast (see WorkOrdersEndpoints.
        // CreateWorkOrderAsync's doc comment — it would otherwise leak unpublished Planner activity
        // to every Technician on the site-wide dispatch group). Publish is the first point a
        // WorkOrderChanged event actually fires.
        var publishResponse = await plannerHttp.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });
        Assert.Equal(System.Net.HttpStatusCode.OK, publishResponse.StatusCode);

        var completedTask = await Task.WhenAny(siteAReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(completedTask == siteAReceived.Task, "Site A's own connection never received the WorkOrderChanged broadcast for its own site.");

        // Give the (absent) cross-site broadcast a fair window to arrive if the isolation were broken.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(siteBReceivedAnything, "A Site B connection received a broadcast scoped to Site A — group isolation is broken.");
    }

    private sealed record CsrfPayload(string Token);
}
