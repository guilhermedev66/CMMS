using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cmms.Modules.Assets.Domain;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Cmms.Modules.PreventiveMaintenance.Domain;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// M5's DoD: "every KPI formula is documented and matches its cited industry definition; dashboard
/// numbers reconcile against raw work-order/downtime data in a test" (src/Cmms.Api/ReportingEndpoints.cs).
/// Rather than hand-computing expected hours against wall-clock timing (inherently flaky), these
/// tests independently re-derive each figure from the same raw rows via a *separate* query path
/// than the endpoint uses, and assert the endpoint's response matches — proving the endpoint
/// actually reflects the database's raw state, not just that the formula is self-consistent with
/// itself.
/// </summary>
[Collection("Postgres")]
public sealed class ReportingTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteAId;
    private Guid _assetAId;
    private string _plannerEmail = string.Empty;
    private string _technicianEmail = string.Empty;

    public ReportingTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _factory = new CmmsWebApplicationFactory(_postgres.ConnectionString);
        using (_factory.CreateClient())
        {
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _plannerEmail = $"planner.rpt.{suffix}@example.test";
        _technicianEmail = $"tech.rpt.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
        var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        var siteA = new Site($"SITE-RPT-{suffix}", "Site Reporting", "UTC");
        identityDb.Sites.Add(siteA);
        await identityDb.SaveChangesAsync();
        _siteAId = siteA.Id;

        var asset = new Asset(_siteAId, $"PUMP-{suffix}", "Reporting Test Pump", "Rotating Equipment", AssetCriticality.B);
        assetsDb.Assets.Add(asset);
        await assetsDb.SaveChangesAsync();
        _assetAId = asset.Id;

        var planner = new ApplicationUser(_plannerEmail, "Planner Rpt");
        Assert.True((await userManager.CreateAsync(planner, Password)).Succeeded);
        var technician = new ApplicationUser(_technicianEmail, "Technician Rpt");
        Assert.True((await userManager.CreateAsync(technician, Password)).Succeeded);

        identityDb.SiteMemberships.Add(new SiteMembership(planner.Id, _siteAId, RoleCode.Planner));
        identityDb.SiteMemberships.Add(new SiteMembership(technician.Id, _siteAId, RoleCode.Technician));
        await identityDb.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> CreateAndCompleteWorkOrderAsync(HttpClient plannerClient, HttpClient technicianClient)
    {
        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = $"Rpt test {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            assetId = _assetAId,
            locationId = (Guid?)null
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();
        await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });
        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { });
        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/start", new { });
        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        return workOrderId;
    }

    private async Task MarkPreventiveAsync(Guid workOrderId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var plansDb = scope.ServiceProvider.GetRequiredService<PreventiveMaintenanceDbContext>();
        var plan = new MaintenancePlan(_siteAId, _assetAId, "Monthly PM", null, RecurrenceType.Fixed, 30, 0, DateTimeOffset.UtcNow, Guid.NewGuid());
        plansDb.MaintenancePlans.Add(plan);
        plansDb.MaintenancePlanOccurrences.Add(new MaintenancePlanOccurrence(plan.Id, _siteAId, DateTimeOffset.UtcNow, workOrderId));
        await plansDb.SaveChangesAsync();
    }

    [Fact]
    public async Task Dashboard_reconciles_preventive_corrective_split_and_parts_cost_against_raw_rows()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var correctiveId = await CreateAndCompleteWorkOrderAsync(plannerClient, technicianClient);
        var preventiveId = await CreateAndCompleteWorkOrderAsync(plannerClient, technicianClient);
        await MarkPreventiveAsync(preventiveId);

        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{correctiveId}/part-usages", new
        {
            partName = "Filter", partCode = (string?)null, quantity = 3m, unitCost = 12.5m, currency = "USD", idempotencyKey = (string?)null
        });
        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{preventiveId}/part-usages", new
        {
            partName = "Belt", partCode = (string?)null, quantity = 1m, unitCost = 40m, currency = "USD", idempotencyKey = (string?)null
        });

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);
        var response = await plannerClient.GetAsync(
            $"/reports/kpis?siteId={_siteAId}&fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var kpis = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Independent re-derivation, straight from the raw tables, via a query path the endpoint
        // itself doesn't share (no reuse of ReportingEndpoints' own LINQ).
        await using var scope = _factory.Services.CreateAsyncScope();
        var workOrdersDb = scope.ServiceProvider.GetRequiredService<WorkManagementDbContext>();
        var expectedPartsCost = await workOrdersDb.PartUsages
            .Where(p => p.SiteId == _siteAId && p.CreatedAtUtc >= from && p.CreatedAtUtc < to)
            .SumAsync(p => p.Quantity * p.UnitCost);

        Assert.Equal(1, kpis.GetProperty("preventiveWorkOrderCount").GetInt32());
        Assert.Equal(1, kpis.GetProperty("correctiveWorkOrderCount").GetInt32());
        Assert.False(kpis.GetProperty("costsMasked").GetBoolean());
        Assert.Equal(expectedPartsCost, kpis.GetProperty("totalPartsCost").GetDecimal());
        Assert.Equal(97.5m, expectedPartsCost); // 3*12.5 + 1*40 — sanity-checks the independent query itself.
    }

    [Fact]
    public async Task A_cancelled_work_order_never_contributes_to_completed_period_figures()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);

        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = $"Cancelled {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            assetId = _assetAId,
            locationId = (Guid?)null
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();
        await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/cancel", new { reason = "No longer needed" });

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);
        var response = await plannerClient.GetAsync(
            $"/reports/kpis?siteId={_siteAId}&fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}");
        var kpis = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, kpis.GetProperty("preventiveWorkOrderCount").GetInt32());
        Assert.Equal(0, kpis.GetProperty("correctiveWorkOrderCount").GetInt32());
        // Cancelled orders also never appear in the open-backlog snapshot (Open/Scheduled/InProgress only).
        Assert.Equal(0, kpis.GetProperty("openBacklogCount").GetInt32());
    }

    [Fact]
    public async Task Mtbf_and_related_figures_are_null_not_zero_when_the_asset_has_zero_failures_in_the_window()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        // A completed Work Order with no downtime at all is not a "failure" — MTBF/MTTR/Inherent
        // Availability must come back null (undefined), never 0 or Infinity (docs/01's explicit
        // requirement), while Operational Availability (which needs no failure count) is still a
        // real, defined number.
        await CreateAndCompleteWorkOrderAsync(plannerClient, technicianClient);

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);
        var response = await plannerClient.GetAsync(
            $"/reports/kpis?siteId={_siteAId}&assetId={_assetAId}&fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}");
        var kpis = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, kpis.GetProperty("mtbfHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, kpis.GetProperty("mttrHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, kpis.GetProperty("inherentAvailability").ValueKind);
        Assert.Equal(JsonValueKind.Number, kpis.GetProperty("operationalAvailability").ValueKind);
        Assert.True(kpis.GetProperty("operationalAvailability").GetDouble() is > 0.99 and <= 1.0);
    }

    [Fact]
    public async Task Mtbf_figures_are_null_when_no_asset_is_specified_never_averaged_across_assets()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);
        var response = await plannerClient.GetAsync(
            $"/reports/kpis?siteId={_siteAId}&fromUtc={Uri.EscapeDataString(from.ToString("O"))}&toUtc={Uri.EscapeDataString(to.ToString("O"))}");
        var kpis = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, kpis.GetProperty("mtbfHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, kpis.GetProperty("operationalAvailability").ValueKind);
    }

    [Fact]
    public async Task Technician_cannot_see_the_operational_dashboard()
    {
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var response = await technicianClient.GetAsync($"/reports/kpis?siteId={_siteAId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_inverted_date_range_is_rejected_as_a_validation_error()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);

        var now = DateTimeOffset.UtcNow;
        var response = await plannerClient.GetAsync(
            $"/reports/kpis?siteId={_siteAId}&fromUtc={Uri.EscapeDataString(now.ToString("O"))}&toUtc={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
