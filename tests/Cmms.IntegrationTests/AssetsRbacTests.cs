using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cmms.Modules.Assets.Domain;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// Proves the site-scoped RBAC wiring in src/Cmms.Api/AssetsEndpoints.cs end-to-end over real
/// HTTP + cookie auth against the real endpoints — not a re-implementation of the permission
/// check in test code. Covers docs/02-security-and-invariants.md's IDOR row: "cross-site object
/// reference ... not-found and forbidden responses look identical to avoid confirming existence."
/// </summary>
[Collection("Postgres")]
public sealed class AssetsRbacTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteAId;
    private Guid _siteBId;
    private string _plannerAEmail = string.Empty;
    private string _technicianBEmail = string.Empty;

    public AssetsRbacTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        _factory = new CmmsWebApplicationFactory(_postgres.ConnectionString);

        // Touch the factory once to force the host (and its DI container) to build.
        using (_factory.CreateClient())
        {
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _plannerAEmail = $"planner.a.{suffix}@example.test";
        _technicianBEmail = $"tech.b.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();

        var siteA = new Site($"SITE-A-{suffix}", "Site A", "UTC");
        var siteB = new Site($"SITE-B-{suffix}", "Site B", "UTC");
        db.Sites.AddRange(siteA, siteB);
        await db.SaveChangesAsync();
        _siteAId = siteA.Id;
        _siteBId = siteB.Id;

        var plannerA = new ApplicationUser(_plannerAEmail, "Planner A");
        var plannerResult = await userManager.CreateAsync(plannerA, Password);
        Assert.True(plannerResult.Succeeded, string.Join("; ", plannerResult.Errors.Select(e => e.Description)));

        var technicianB = new ApplicationUser(_technicianBEmail, "Technician B");
        var technicianResult = await userManager.CreateAsync(technicianB, Password);
        Assert.True(technicianResult.Succeeded, string.Join("; ", technicianResult.Errors.Select(e => e.Description)));

        db.SiteMemberships.Add(new SiteMembership(plannerA.Id, siteA.Id, RoleCode.Planner));
        db.SiteMemberships.Add(new SiteMembership(technicianB.Id, siteB.Id, RoleCode.Technician));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task User_outside_the_assets_site_cannot_read_or_edit_it_while_the_owning_planner_can()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);

        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/assets", new
        {
            siteId = _siteAId,
            tag = $"PUMP-{Guid.NewGuid():N}"[..12],
            name = "Cooling Pump",
            category = "Rotating Equipment",
            criticality = "B"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var assetId = created.GetProperty("id").GetGuid();

        // Positive control: the owning Planner can read their own site's asset.
        var ownerGet = await plannerClient.GetAsync($"/assets/{assetId}");
        Assert.Equal(HttpStatusCode.OK, ownerGet.StatusCode);

        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianBEmail, Password);

        // Negative: a Technician who is only a member of Site B gets a 404 for a Site A asset —
        // not a 403 — so the response can't be used to fingerprint whether the id exists at all.
        var crossSiteGet = await technicianClient.GetAsync($"/assets/{assetId}");
        Assert.Equal(HttpStatusCode.NotFound, crossSiteGet.StatusCode);

        var crossSiteEdit = await technicianClient.PutJsonWithCsrfAsync($"/assets/{assetId}", new
        {
            name = "Tampered Name",
            category = "Rotating Equipment",
            status = "InService"
        });
        Assert.Equal(HttpStatusCode.NotFound, crossSiteEdit.StatusCode);

        // Confirm the edit attempt genuinely did not land: re-read as the owning Planner.
        var verifyResponse = await plannerClient.GetAsync($"/assets/{assetId}");
        var verified = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cooling Pump", verified.GetProperty("name").GetString());
    }

    [Fact]
    public async Task List_assets_is_scoped_to_the_callers_own_site_memberships()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);

        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/assets", new
        {
            siteId = _siteAId,
            tag = $"CONV-{Guid.NewGuid():N}"[..12],
            name = "Conveyor",
            category = "Material Handling",
            criticality = "A"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Seed a Site B asset directly (Technician has no assets.create permission at all) so the
        // list assertion below proves real filtering, not just an empty result.
        var siteBAssetTag = $"FAN-{Guid.NewGuid():N}"[..12];
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            assetsDb.Assets.Add(new Asset(_siteBId, siteBAssetTag, "Exhaust Fan", "HVAC", AssetCriticality.C));
            await assetsDb.SaveChangesAsync();
        }

        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianBEmail, Password);

        var listResponse = await technicianClient.GetAsync("/assets");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rows = list.EnumerateArray().ToList();

        // Technician B (Site B member only) sees exactly their own site's asset...
        Assert.Contains(rows, element => element.GetProperty("siteId").GetGuid() == _siteBId);
        // ...and never Site A's, even though it exists and the endpoint call itself succeeds.
        Assert.All(rows, element => Assert.NotEqual(_siteAId, element.GetProperty("siteId").GetGuid()));
    }

    /// <summary>
    /// Regression test for a Codex QA M1 smoke-pass BLOCKER: CreateAssetAsync validated
    /// CurrentLocationId's site but not ParentAssetId's, so a cross-site parent reference fell
    /// through to the DB's composite FK constraint as an unhandled 500 instead of a clean 400.
    /// </summary>
    [Fact]
    public async Task Create_asset_rejects_a_parent_asset_from_a_different_site()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerAEmail, Password);

        // A real asset that exists, is a valid parent shape, but belongs to Site B.
        Guid siteBAssetId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var siteBAsset = new Asset(_siteBId, $"MOTOR-{Guid.NewGuid():N}"[..12], "Drive Motor", "Rotating Equipment", AssetCriticality.B);
            assetsDb.Assets.Add(siteBAsset);
            await assetsDb.SaveChangesAsync();
            siteBAssetId = siteBAsset.Id;
        }

        var response = await plannerClient.PostJsonWithCsrfAsync("/assets", new
        {
            siteId = _siteAId,
            tag = $"SKID-{Guid.NewGuid():N}"[..12],
            name = "Pump Skid",
            category = "Rotating Equipment",
            criticality = "B",
            parentAssetId = siteBAssetId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
