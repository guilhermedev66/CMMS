using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// M2's flagship concurrency proof, per docs/02-security-and-invariants.md § "Concurrency &amp;
/// invariants": "Two technicians claim the same Work Order" — the brief's own example ("duas
/// pessoas tentando assumir a mesma OS") — must resolve to exactly one winner via the atomic
/// conditional <c>UPDATE</c> in src/Cmms.Api/WorkOrdersEndpoints.cs's SelfClaimAsync, not a
/// read-then-write race. This drives two real concurrent HTTP requests against the real running
/// host + real PostgreSQL — not a single-threaded simulation — so the assertion is actually about
/// database-level atomicity, not application-level sequencing.
///
/// Also covers the M2 DoD's "state machine cannot be forced into an invalid transition via API"
/// requirement for the ordinary (non-self-claim) ordinary-transition path.
/// </summary>
[Collection("Postgres")]
public sealed class WorkOrdersConcurrencyTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteAId;
    private string _plannerAEmail = string.Empty;
    private string _technicianA1Email = string.Empty;
    private string _technicianA2Email = string.Empty;

    public WorkOrdersConcurrencyTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        _factory = new CmmsWebApplicationFactory(_postgres.ConnectionString);
        using (_factory.CreateClient())
        {
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _plannerAEmail = $"planner.wo.{suffix}@example.test";
        _technicianA1Email = $"tech.wo1.{suffix}@example.test";
        _technicianA2Email = $"tech.wo2.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();

        var siteA = new Site($"SITE-WO-{suffix}", "Site WO", "UTC");
        db.Sites.Add(siteA);
        await db.SaveChangesAsync();
        _siteAId = siteA.Id;

        var plannerA = new ApplicationUser(_plannerAEmail, "Planner WO");
        Assert.True((await userManager.CreateAsync(plannerA, Password)).Succeeded);
        var technicianA1 = new ApplicationUser(_technicianA1Email, "Technician WO 1");
        Assert.True((await userManager.CreateAsync(technicianA1, Password)).Succeeded);
        var technicianA2 = new ApplicationUser(_technicianA2Email, "Technician WO 2");
        Assert.True((await userManager.CreateAsync(technicianA2, Password)).Succeeded);

        db.SiteMemberships.Add(new SiteMembership(plannerA.Id, siteA.Id, RoleCode.Planner));
        db.SiteMemberships.Add(new SiteMembership(technicianA1.Id, siteA.Id, RoleCode.Technician));
        db.SiteMemberships.Add(new SiteMembership(technicianA2.Id, siteA.Id, RoleCode.Technician));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<Guid> CreateOpenWorkOrderAsync(HttpClient plannerClient)
    {
        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = $"Pump inspection {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            assetId = (Guid?)null,
            locationId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();

        var publishResponse = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        return workOrderId;
    }

    [Fact]
    public async Task Two_technicians_racing_to_self_claim_the_same_open_work_order_resolve_to_exactly_one_winner()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);
        var workOrderId = await CreateOpenWorkOrderAsync(plannerClient);

        using var technician1Client = _factory.CreateClient();
        await technician1Client.LoginAsync(_technicianA1Email, Password);
        var csrf1 = await technician1Client.GetCsrfTokenAsync();

        using var technician2Client = _factory.CreateClient();
        await technician2Client.LoginAsync(_technicianA2Email, Password);
        var csrf2 = await technician2Client.GetCsrfTokenAsync();

        Task<HttpResponseMessage> ClaimAsync(HttpClient client, string csrfToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/work-orders/{workOrderId}/self-claim");
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
            return client.SendAsync(request);
        }

        // Fire both claim attempts at the same time — the race actually happens inside Postgres,
        // not in this test's own scheduling, but launching them together maximizes the chance both
        // requests are genuinely in flight concurrently rather than trivially serialized by .NET's
        // own task scheduler.
        var claim1Task = ClaimAsync(technician1Client, csrf1);
        var claim2Task = ClaimAsync(technician2Client, csrf2);
        var results = await Task.WhenAll(claim1Task, claim2Task);

        var successCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        // Confirm the persisted state actually reflects exactly one winner, not just the HTTP
        // response — a bug that returned the right status codes but left both/neither assignee set
        // would otherwise slip through.
        var finalState = await plannerClient.GetAsync($"/work-orders/{workOrderId}");
        var finalWorkOrder = await finalState.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scheduled", finalWorkOrder.GetProperty("status").GetString());
        var assigneeId = finalWorkOrder.GetProperty("assigneeId").GetGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var technician1Id = (await userManager.FindByEmailAsync(_technicianA1Email))!.Id;
        var technician2Id = (await userManager.FindByEmailAsync(_technicianA2Email))!.Id;
        Assert.True(assigneeId == technician1Id || assigneeId == technician2Id);
    }

    [Fact]
    public async Task Self_claim_after_someone_else_already_claimed_returns_conflict_not_an_error()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);
        var workOrderId = await CreateOpenWorkOrderAsync(plannerClient);

        using var technician1Client = _factory.CreateClient();
        await technician1Client.LoginAsync(_technicianA1Email, Password);
        var firstClaim = await technician1Client.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { });
        Assert.Equal(HttpStatusCode.OK, firstClaim.StatusCode);

        using var technician2Client = _factory.CreateClient();
        await technician2Client.LoginAsync(_technicianA2Email, Password);
        var secondClaim = await technician2Client.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { });
        Assert.Equal(HttpStatusCode.Conflict, secondClaim.StatusCode);
    }

    [Fact]
    public async Task Completing_a_work_order_that_was_never_started_is_rejected_as_a_conflict()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);
        var workOrderId = await CreateOpenWorkOrderAsync(plannerClient);

        // Still Open (never self-claimed/started) — Complete requires InProgress per
        // docs/01's transition table. This must be a clean 409, not a 500.
        var completeResponse = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.Conflict, completeResponse.StatusCode);

        var stateResponse = await plannerClient.GetAsync($"/work-orders/{workOrderId}");
        var state = await stateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Open", state.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Publishing_an_already_published_work_order_is_rejected_as_a_conflict()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);
        var workOrderId = await CreateOpenWorkOrderAsync(plannerClient);

        var secondPublish = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });
        Assert.Equal(HttpStatusCode.Conflict, secondPublish.StatusCode);
    }
}
