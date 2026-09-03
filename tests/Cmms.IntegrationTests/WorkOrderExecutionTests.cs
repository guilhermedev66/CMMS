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
/// M4's checklist/downtime/parts execution endpoints (src/Cmms.Api/WorkOrderExecutionEndpoints.cs)
/// and the Mark Completed guard they feed (src/Modules/WorkManagement/Domain/WorkOrder.cs). Drives
/// real HTTP requests against the real running host + real PostgreSQL, same style as
/// <see cref="WorkOrdersConcurrencyTests"/>.
/// </summary>
[Collection("Postgres")]
public sealed class WorkOrderExecutionTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteAId;
    private Guid _assetAId;
    private string _plannerEmail = string.Empty;
    private string _technicianEmail = string.Empty;
    private string _otherTechnicianEmail = string.Empty;

    public WorkOrderExecutionTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _factory = new CmmsWebApplicationFactory(_postgres.ConnectionString);
        using (_factory.CreateClient())
        {
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _plannerEmail = $"planner.exec.{suffix}@example.test";
        _technicianEmail = $"tech.exec.{suffix}@example.test";
        _otherTechnicianEmail = $"tech.exec2.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
        var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        var siteA = new Site($"SITE-EXEC-{suffix}", "Site Exec", "UTC");
        identityDb.Sites.Add(siteA);
        await identityDb.SaveChangesAsync();
        _siteAId = siteA.Id;

        var asset = new Asset(_siteAId, $"PUMP-{suffix}", "Execution Test Pump", "Rotating Equipment", AssetCriticality.B);
        assetsDb.Assets.Add(asset);
        await assetsDb.SaveChangesAsync();
        _assetAId = asset.Id;

        var planner = new ApplicationUser(_plannerEmail, "Planner Exec");
        Assert.True((await userManager.CreateAsync(planner, Password)).Succeeded);
        var technician = new ApplicationUser(_technicianEmail, "Technician Exec");
        Assert.True((await userManager.CreateAsync(technician, Password)).Succeeded);
        var otherTechnician = new ApplicationUser(_otherTechnicianEmail, "Technician Exec 2");
        Assert.True((await userManager.CreateAsync(otherTechnician, Password)).Succeeded);

        identityDb.SiteMemberships.Add(new SiteMembership(planner.Id, _siteAId, RoleCode.Planner));
        identityDb.SiteMemberships.Add(new SiteMembership(technician.Id, _siteAId, RoleCode.Technician));
        identityDb.SiteMemberships.Add(new SiteMembership(otherTechnician.Id, _siteAId, RoleCode.Technician));
        await identityDb.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> CreateInProgressWorkOrderAsync(HttpClient plannerClient, HttpClient technicianClient)
    {
        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = $"Exec test {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            assetId = _assetAId,
            locationId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/start", new { })).StatusCode);

        return workOrderId;
    }

    [Fact]
    public async Task Mark_completed_is_rejected_while_a_required_checklist_item_is_unresolved()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        var createItem = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/checklist-items", new
        {
            itemType = "Boolean",
            label = "Guard in place",
            isRequired = true
        });
        Assert.Equal(HttpStatusCode.Created, createItem.StatusCode);
        var item = await createItem.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = item.GetProperty("id").GetGuid();

        var prematureComplete = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.Conflict, prematureComplete.StatusCode);

        var resolve = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/checklist-items/{itemId}/resolve",
            new { booleanValue = true, numericValue = (decimal?)null, selectedOption = (string?)null, noteText = (string?)null, attachmentId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        var completeNow = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.OK, completeNow.StatusCode);
    }

    [Fact]
    public async Task Mark_completed_is_rejected_while_a_downtime_interval_is_still_open()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        var open = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/downtime-intervals", new { classification = "FullStop" });
        Assert.Equal(HttpStatusCode.Created, open.StatusCode);
        var interval = await open.Content.ReadFromJsonAsync<JsonElement>();
        var intervalId = interval.GetProperty("id").GetGuid();

        var prematureComplete = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.Conflict, prematureComplete.StatusCode);

        var close = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/downtime-intervals/{intervalId}/close",
            new { causeCategory = "Mechanical", causeMechanism = "Bearing wear" });
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        var completeNow = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.OK, completeNow.StatusCode);
    }

    [Fact]
    public async Task Two_overlapping_fullstop_downtime_intervals_on_the_same_asset_are_rejected_by_the_database()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        var first = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/downtime-intervals", new { classification = "FullStop" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Same asset, still open — the exclusion constraint (ex_downtime_intervals_fullstop_no_overlap)
        // must reject this as a 409, not a 500, regardless of which Work Order it's opened against.
        var second = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/downtime-intervals", new { classification = "FullStop" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Part_usage_idempotency_key_deduplicates_a_retried_posting()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var request = new { partName = "Bearing 6205", partCode = "BRG-6205", quantity = 2m, unitCost = 15.5m, currency = "USD", idempotencyKey };

        var first = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/part-usages", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = firstBody.GetProperty("id").GetGuid();

        var retry = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/part-usages", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryBody = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstId, retryBody.GetProperty("id").GetGuid());

        var list = await technicianClient.GetFromJsonAsync<JsonElement[]>($"/work-orders/{workOrderId}/part-usages");
        Assert.Single(list!);
    }

    [Fact]
    public async Task Costs_are_masked_for_a_caller_without_costs_view_permission()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);
        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/part-usages", new
        {
            partName = "Gasket", partCode = (string?)null, quantity = 1m, unitCost = 42m, currency = "USD", idempotencyKey = (string?)null
        });

        var technicianView = (await technicianClient.GetFromJsonAsync<JsonElement[]>($"/work-orders/{workOrderId}/part-usages"))!.Single();
        Assert.Equal(JsonValueKind.Null, technicianView.GetProperty("unitCost").ValueKind);

        var plannerView = (await plannerClient.GetFromJsonAsync<JsonElement[]>($"/work-orders/{workOrderId}/part-usages"))!.Single();
        Assert.Equal(42m, plannerView.GetProperty("unitCost").GetDecimal());
    }

    [Fact]
    public async Task A_technician_cannot_define_checklist_items_only_resolve_them()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        var response = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/checklist-items", new
        {
            itemType = "Boolean",
            label = "Should be rejected",
            isRequired = false
        });

        // Not-found, not forbidden — docs/02: cross-permission denial looks identical to not-found.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_technician_who_is_not_the_assignee_cannot_resolve_someone_elses_checklist_item()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);
        using var otherTechnicianClient = _factory.CreateClient();
        await otherTechnicianClient.LoginAsync(_otherTechnicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);
        var createItem = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/checklist-items", new
        {
            itemType = "Note",
            label = "Observation",
            isRequired = false
        });
        var item = await createItem.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = item.GetProperty("id").GetGuid();

        var response = await otherTechnicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/checklist-items/{itemId}/resolve",
            new { booleanValue = (bool?)null, numericValue = (decimal?)null, selectedOption = (string?)null, noteText = "Sneaky edit", attachmentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
