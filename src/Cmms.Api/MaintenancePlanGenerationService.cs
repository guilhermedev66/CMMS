using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cmms.Api;

/// <summary>
/// Thin timer wrapper around <see cref="IMaintenancePlanGenerationRunner"/> — all the
/// correctness-critical locking/idempotency logic lives in the runner (directly unit/integration
/// testable without a real timer); this type only owns the schedule and the per-tick DI scope
/// (the runner's dependencies, like every DbContext in this codebase, are request/scope-lifetime,
/// so a singleton <see cref="BackgroundService"/> can't hold one across ticks).
/// </summary>
public sealed class MaintenancePlanGenerationService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MaintenancePlanGenerationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue("PreventiveMaintenance:SweepIntervalSeconds", 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)));

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<IMaintenancePlanGenerationRunner>();
                var generated = await runner.RunSweepAsync(stoppingToken);
                if (generated > 0)
                {
                    logger.LogInformation("Preventive maintenance sweep generated {Count} occurrence(s).", generated);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed sweep must not crash the host or stop future sweeps — the next tick
                // re-evaluates the same due plans from scratch (idempotent by construction, per
                // IMaintenancePlanGenerationRunner's doc comment), so a transient DB blip here just
                // means the generation is a little late, not lost.
                logger.LogError(ex, "Preventive maintenance sweep failed; will retry next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
