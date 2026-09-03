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
/// M2's Codex QA adversarial-pass coverage (docs/06-milestones.md's M2 DoD: "IDOR, authz bypass,
/// invalid transitions with no BLOCKER") for Maintenance Requests and Work Orders, following the
/// same real-HTTP-against-real-endpoints approach as AssetsRbacTests.
/// </summary>
[Collection("Postgres")]
public sealed class MaintenanceRequestsAndWorkOrdersRbacTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteAId;
    private Guid _siteBId;
    private string _plannerAEmail = string.Empty;
    private string _requesterAEmail = string.Empty;
    private string _requesterA2Email = string.Empty;
    private string _technicianAEmail = string.Empty;
    private string _technicianBEmail = string.Empty;

    public MaintenanceRequestsAndWorkOrdersRbacTests(PostgresFixture postgres)
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
        _plannerAEmail = $"planner.rbac.{suffix}@example.test";
        _requesterAEmail = $"requester.rbac.{suffix}@example.test";
        _requesterA2Email = $"requester2.rbac.{suffix}@example.test";
        _technicianAEmail = $"tech.rbac.a.{suffix}@example.test";
        _technicianBEmail = $"tech.rbac.b.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();

        var siteA = new Site($"SITE-RBAC-A-{suffix}", "Site RBAC A", "UTC");
        var siteB = new Site($"SITE-RBAC-B-{suffix}", "Site RBAC B", "UTC");
        db.Sites.AddRange(siteA, siteB);
        await db.SaveChangesAsync();
        _siteAId = siteA.Id;
        _siteBId = siteB.Id;

        var plannerA = new ApplicationUser(_plannerAEmail, "Planner RBAC A");
        Assert.True((await userManager.CreateAsync(plannerA, Password)).Succeeded);
        var requesterA = new ApplicationUser(_requesterAEmail, "Requester RBAC A");
        Assert.True((await userManager.CreateAsync(requesterA, Password)).Succeeded);
        var requesterA2 = new ApplicationUser(_requesterA2Email, "Requester RBAC A2");
        Assert.True((await userManager.CreateAsync(requesterA2, Password)).Succeeded);
        var technicianA = new ApplicationUser(_technicianAEmail, "Technician RBAC A");
        Assert.True((await userManager.CreateAsync(technicianA, Password)).Succeeded);
        var technicianB = new ApplicationUser(_technicianBEmail, "Technician RBAC B");
        Assert.True((await userManager.CreateAsync(technicianB, Password)).Succeeded);

        db.SiteMemberships.Add(new SiteMembership(plannerA.Id, siteA.Id, RoleCode.Planner));
        db.SiteMemberships.Add(new SiteMembership(requesterA.Id, siteA.Id, RoleCode.Requester));
        db.SiteMemberships.Add(new SiteMembership(requesterA2.Id, siteA.Id, RoleCode.Requester));
        db.SiteMemberships.Add(new SiteMembership(technicianA.Id, siteA.Id, RoleCode.Technician));
        db.SiteMemberships.Add(new SiteMembership(technicianB.Id, siteB.Id, RoleCode.Technician));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<(Guid Id, Guid LocationId)> CreateRequestAsync(HttpClient requesterClient, Guid siteId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var assetsDb = scope.ServiceProvider.GetRequiredService<Cmms.Modules.Assets.Infrastructure.AssetsDbContext>();
        var location = new Cmms.Modules.Assets.Domain.Location(siteId, $"LOC-{Guid.NewGuid():N}"[..12], "Test Location");
        assetsDb.Locations.Add(location);
        await assetsDb.SaveChangesAsync();

        var createResponse = await requesterClient.PostJsonWithCsrfAsync("/requests", new
        {
            siteId,
            title = $"Leaking valve {Guid.NewGuid():N}"[..25],
            description = (string?)null,
            assetId = (Guid?)null,
            locationId = location.Id
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (created.GetProperty("id").GetGuid(), location.Id);
    }

    [Fact]
    public async Task Technician_outside_the_work_orders_site_cannot_read_or_self_claim_it()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);

        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = $"Belt replacement {Guid.NewGuid():N}"[..25],
            description = (string?)null,
            assetId = (Guid?)null,
            locationId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();
        await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });

        using var outsiderClient = _factory.CreateClient();
        await outsiderClient.LoginAsync(_technicianBEmail, Password);

        var getResponse = await outsiderClient.GetAsync($"/work-orders/{workOrderId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var claimResponse = await outsiderClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { });
        Assert.Equal(HttpStatusCode.NotFound, claimResponse.StatusCode);

        // Confirm the attempted claim genuinely did not land.
        var verify = await plannerClient.GetAsync($"/work-orders/{workOrderId}");
        var verified = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Open", verified.GetProperty("status").GetString());
        Assert.True(verified.GetProperty("assigneeId").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task A_technician_who_is_not_the_assignee_cannot_start_or_complete_someone_elses_work_order()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);

        await using var seedScope = _factory.Services.CreateAsyncScope();
        var userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var technicianAId = (await userManager.FindByEmailAsync(_technicianAEmail))!.Id;

        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = $"Motor overhaul {Guid.NewGuid():N}"[..25],
            description = (string?)null,
            assetId = (Guid?)null,
            locationId = (Guid?)null
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();
        await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });

        using var technicianAClient = _factory.CreateClient();
        await technicianAClient.LoginAsync(_technicianAEmail, Password);
        var claimResponse = await technicianAClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { });
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);

        // A second Site A technician (never assigned) tries to start the order the first
        // technician just claimed. Same-site membership is not enough — must be the assignee, or
        // Planner/Admin, per docs/01's transition-table actor column.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var bystanderEmail = $"tech.bystander.{suffix}@example.test";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var scopedUserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var scopedDb = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
            var bystander = new ApplicationUser(bystanderEmail, "Technician Bystander");
            Assert.True((await scopedUserManager.CreateAsync(bystander, Password)).Succeeded);
            scopedDb.SiteMemberships.Add(new SiteMembership(bystander.Id, _siteAId, RoleCode.Technician));
            await scopedDb.SaveChangesAsync();
        }

        using var bystanderClient = _factory.CreateClient();
        await bystanderClient.LoginAsync(bystanderEmail, Password);

        var startResponse = await bystanderClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/start", new { });
        Assert.Equal(HttpStatusCode.NotFound, startResponse.StatusCode);

        // The actual assignee can start it.
        var realStartResponse = await technicianAClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/start", new { });
        Assert.Equal(HttpStatusCode.OK, realStartResponse.StatusCode);

        var completeByBystander = await bystanderClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.NotFound, completeByBystander.StatusCode);
    }

    [Fact]
    public async Task Requester_cannot_cancel_another_requesters_new_request_even_though_both_are_site_members()
    {
        using var requesterClient = _factory.CreateClient();
        await requesterClient.LoginAsync(_requesterAEmail, Password);
        var (requestId, _) = await CreateRequestAsync(requesterClient, _siteAId);

        using var otherRequesterClient = _factory.CreateClient();
        await otherRequesterClient.LoginAsync(_requesterA2Email, Password);

        // requests.cancel.own has no "any" counterpart (docs/02's permission catalog) — even
        // though both are Requesters at the same site, this must fail, not silently cancel.
        var cancelResponse = await otherRequesterClient.PostJsonWithCsrfAsync($"/requests/{requestId}/cancel", new { });
        Assert.Equal(HttpStatusCode.NotFound, cancelResponse.StatusCode);

        var ownCancelResponse = await requesterClient.PostJsonWithCsrfAsync($"/requests/{requestId}/cancel", new { });
        Assert.Equal(HttpStatusCode.OK, ownCancelResponse.StatusCode);
    }

    [Fact]
    public async Task Converting_an_already_resolved_request_is_rejected_as_a_conflict_not_a_duplicate_work_order()
    {
        using var requesterClient = _factory.CreateClient();
        await requesterClient.LoginAsync(_requesterAEmail, Password);
        var (requestId, _) = await CreateRequestAsync(requesterClient, _siteAId);

        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);

        var firstConvert = await plannerClient.PostJsonWithCsrfAsync($"/requests/{requestId}/convert", new { title = (string?)null });
        Assert.Equal(HttpStatusCode.OK, firstConvert.StatusCode);

        var secondConvert = await plannerClient.PostJsonWithCsrfAsync($"/requests/{requestId}/convert", new { title = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, secondConvert.StatusCode);

        var rejectAfterConvert = await plannerClient.PostJsonWithCsrfAsync($"/requests/{requestId}/reject", new { reason = "too late" });
        Assert.Equal(HttpStatusCode.Conflict, rejectAfterConvert.StatusCode);
    }

    [Fact]
    public async Task Requester_cannot_read_another_requesters_request_and_planner_can_read_any_request_at_their_site()
    {
        using var requesterClient = _factory.CreateClient();
        await requesterClient.LoginAsync(_requesterAEmail, Password);
        var (requestId, _) = await CreateRequestAsync(requesterClient, _siteAId);

        using var otherRequesterClient = _factory.CreateClient();
        await otherRequesterClient.LoginAsync(_requesterA2Email, Password);
        var crossReadResponse = await otherRequesterClient.GetAsync($"/requests/{requestId}");
        Assert.Equal(HttpStatusCode.NotFound, crossReadResponse.StatusCode);

        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);
        var plannerReadResponse = await plannerClient.GetAsync($"/requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, plannerReadResponse.StatusCode);
    }
}
