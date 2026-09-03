using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cmms.Api;
using Cmms.Modules.Assets.Domain;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Cmms.Modules.PreventiveMaintenance.Domain;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// M3's flagship idempotency proof, per docs/06-milestones.md's M3 DoD: "a concurrency/idempotency
/// test proves a plan cannot generate two work orders for the same due occurrence even under
/// simulated duplicate trigger/multiple-instance conditions." Calls
/// <see cref="IMaintenancePlanGenerationRunner"/> directly (from two separate DI scopes, run
/// concurrently) rather than waiting on the real timer-driven
/// <see cref="MaintenancePlanGenerationService"/> — the more precise way to simulate "two scheduler
/// ticks" or "two instances" than a wall-clock wait would be, per docs/02's concurrency table row.
/// </summary>
[Collection("Postgres")]
public sealed class MaintenancePlanGenerationTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteId;
    private Guid _assetId;
    private string _plannerEmail = string.Empty;

    public MaintenancePlanGenerationTests(PostgresFixture postgres)
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
        _plannerEmail = $"planner.pm.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
        var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        var site = new Site($"SITE-PM-{suffix}", "Site PM", "UTC");
        identityDb.Sites.Add(site);
        await identityDb.SaveChangesAsync();
        _siteId = site.Id;

        var planner = new ApplicationUser(_plannerEmail, "Planner PM");
        Assert.True((await userManager.CreateAsync(planner, Password)).Succeeded);
        identityDb.SiteMemberships.Add(new SiteMembership(planner.Id, site.Id, RoleCode.Planner));
        await identityDb.SaveChangesAsync();

        var asset = new Asset(site.Id, $"PUMP-{suffix}", "PM Test Pump", "Rotating Equipment", AssetCriticality.B);
        assetsDb.Assets.Add(asset);
        await assetsDb.SaveChangesAsync();
        _assetId = asset.Id;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<Guid> SeedDuePlanAsync(RecurrenceType recurrenceType, int intervalDays, bool paused = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var plansDb = scope.ServiceProvider.GetRequiredService<PreventiveMaintenanceDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var planner = (await userManager.FindByEmailAsync(_plannerEmail))!;

        var plan = new MaintenancePlan(
            _siteId,
            _assetId,
            "Quarterly lubrication",
            "Lubricate bearings and inspect seals.",
            recurrenceType,
            intervalDays,
            generationLeadTimeDays: 0,
            firstDueAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            createdByUserId: planner.Id);
        if (paused)
        {
            plan.Pause();
        }

        plansDb.MaintenancePlans.Add(plan);
        await plansDb.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<int> RunSweepInNewScopeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMaintenancePlanGenerationRunner>();
        return await runner.RunSweepAsync();
    }

    [Fact]
    public async Task Two_concurrent_sweeps_on_the_same_due_plan_generate_exactly_one_occurrence_and_work_order()
    {
        var planId = await SeedDuePlanAsync(RecurrenceType.Fixed, intervalDays: 30);

        // Simulates two scheduler instances (or two overlapping ticks) racing on the same due plan.
        var sweep1 = RunSweepInNewScopeAsync();
        var sweep2 = RunSweepInNewScopeAsync();
        await Task.WhenAll(sweep1, sweep2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var plansDb = scope.ServiceProvider.GetRequiredService<PreventiveMaintenanceDbContext>();

        var occurrences = await plansDb.MaintenancePlanOccurrences
            .Where(o => o.PlanId == planId)
            .ToListAsync();
        Assert.Single(occurrences);

        var plan = await plansDb.MaintenancePlans.AsNoTracking().FirstAsync(p => p.Id == planId);
        Assert.Equal(occurrences[0].Id, plan.ActiveOccurrenceId);
        // Fixed recurrence advances immediately at generation time, from the original due date.
        Assert.True(plan.NextDueAtUtc > DateTimeOffset.UtcNow.AddDays(28));

        // A third sweep (another tick) must still not generate a second occurrence — SuppressIfOpen.
        var thirdSweepGenerated = await RunSweepInNewScopeAsync();
        Assert.Equal(0, thirdSweepGenerated);

        var occurrencesAfterThirdSweep = await plansDb.MaintenancePlanOccurrences
            .Where(o => o.PlanId == planId)
            .CountAsync();
        Assert.Equal(1, occurrencesAfterThirdSweep);
    }

    [Fact]
    public async Task Paused_plan_is_never_swept_even_though_it_is_due()
    {
        var planId = await SeedDuePlanAsync(RecurrenceType.Fixed, intervalDays: 7, paused: true);

        var generated = await RunSweepInNewScopeAsync();
        Assert.Equal(0, generated);

        await using var scope = _factory.Services.CreateAsyncScope();
        var plansDb = scope.ServiceProvider.GetRequiredService<PreventiveMaintenanceDbContext>();
        var hasOccurrence = await plansDb.MaintenancePlanOccurrences.AnyAsync(o => o.PlanId == planId);
        Assert.False(hasOccurrence);
    }

    [Fact]
    public async Task Floating_plan_recomputes_next_due_from_actual_completion_and_clears_the_active_pointer()
    {
        var planId = await SeedDuePlanAsync(RecurrenceType.Floating, intervalDays: 14);

        var generated = await RunSweepInNewScopeAsync();
        Assert.Equal(1, generated);

        Guid workOrderId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var plansDb = scope.ServiceProvider.GetRequiredService<PreventiveMaintenanceDbContext>();
            var occurrence = await plansDb.MaintenancePlanOccurrences.AsNoTracking().FirstAsync(o => o.PlanId == planId);
            workOrderId = occurrence.WorkOrderId;

            var planAfterGeneration = await plansDb.MaintenancePlans.AsNoTracking().FirstAsync(p => p.Id == planId);
            // Floating: next-due is NOT advanced at generation time (only at real completion).
            Assert.True(planAfterGeneration.NextDueAtUtc <= DateTimeOffset.UtcNow);
            Assert.NotNull(planAfterGeneration.ActiveOccurrenceId);
        }

        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);

        var publishResponse = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        // Start requires Scheduled (an assignee), regardless of who's allowed to press Start — the
        // Planner claims it themselves first (WorkOrdersSelfClaim is granted to Planner too, per
        // docs/02's permission table), since this bounded slice has no Planner-direct-assign path.
        var claimResponse = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { });
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var startResponse = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/start", new { });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var completeResponse = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completed = await completeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var completedAtUtc = completed.GetProperty("completedAtUtc").GetDateTimeOffset();

        await using var finalScope = _factory.Services.CreateAsyncScope();
        var finalPlansDb = finalScope.ServiceProvider.GetRequiredService<PreventiveMaintenanceDbContext>();
        var planAfterCompletion = await finalPlansDb.MaintenancePlans.AsNoTracking().FirstAsync(p => p.Id == planId);

        Assert.Null(planAfterCompletion.ActiveOccurrenceId);
        var expectedNextDue = completedAtUtc.AddDays(14);
        Assert.True(Math.Abs((planAfterCompletion.NextDueAtUtc - expectedNextDue).TotalSeconds) < 5);
    }
}
