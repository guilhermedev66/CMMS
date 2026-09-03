namespace Cmms.Modules.PreventiveMaintenance.Domain;

/// <summary>
/// Thrown when a plan mutation is invoked in a state that doesn't allow it (e.g. recording a
/// generation while one is already active). Defense-in-depth: the generation job is expected to
/// hold the plan row's <c>SELECT ... FOR UPDATE</c> lock and re-check state itself before calling
/// a mutator, per docs/01's "Resolves QA finding B-04(2)" protocol — this should never fire in
/// normal operation.
/// </summary>
public sealed class InvalidMaintenancePlanOperationException(string message) : InvalidOperationException(message);

/// <summary>
/// Per docs/01-domain-and-workflows.md § "Preventive maintenance flow" and its "Resolves QA
/// finding B-04(2)" note. <see cref="ActiveOccurrenceId"/> is the pointer that makes
/// <c>SuppressIfOpen</c> actually correct across different due dates (not just the single nominal
/// occurrence date) — while it's set, the generation job does nothing for this plan, regardless of
/// which nominal <see cref="MaintenancePlanOccurrence.ScheduledForUtc"/> generated it.
/// </summary>
public sealed class MaintenancePlan
{
    private MaintenancePlan()
    {
    }

    public MaintenancePlan(
        Guid siteId,
        Guid assetId,
        string title,
        string? description,
        RecurrenceType recurrenceType,
        int intervalDays,
        int generationLeadTimeDays,
        DateTimeOffset firstDueAtUtc,
        Guid createdByUserId)
    {
        if (intervalDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalDays), intervalDays, "Interval must be a positive number of days.");
        }

        if (generationLeadTimeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generationLeadTimeDays), generationLeadTimeDays, "Lead time cannot be negative.");
        }

        Id = Guid.CreateVersion7();
        SiteId = siteId;
        AssetId = assetId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        RecurrenceType = recurrenceType;
        IntervalDays = intervalDays;
        GenerationLeadTimeDays = generationLeadTimeDays;
        Status = MaintenancePlanStatus.Active;
        NextDueAtUtc = firstDueAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        RowVersion = 1;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid AssetId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public RecurrenceType RecurrenceType { get; private set; }

    public int IntervalDays { get; private set; }

    /// <summary>A Work Order due June 15 can be generated June 8 so parts/scheduling happen ahead
    /// of the due date (docs/01).</summary>
    public int GenerationLeadTimeDays { get; private set; }

    public MaintenancePlanStatus Status { get; private set; }

    public DateTimeOffset NextDueAtUtc { get; private set; }

    /// <summary>Points at the currently-open generated <see cref="MaintenancePlanOccurrence"/>, or
    /// null if none is open. See this type's doc comment.</summary>
    public Guid? ActiveOccurrenceId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public long RowVersion { get; private set; }

    public void Pause()
    {
        RequireStatus(MaintenancePlanStatus.Active, nameof(Pause));
        Status = MaintenancePlanStatus.Paused;
    }

    public void Resume()
    {
        RequireStatus(MaintenancePlanStatus.Paused, nameof(Resume));
        Status = MaintenancePlanStatus.Active;
    }

    /// <summary>
    /// Called by the generation job, under the plan row's lock, after it has inserted the
    /// occurrence and created the linked Work Order in the same transaction. Advances
    /// <see cref="NextDueAtUtc"/> immediately for <see cref="RecurrenceType.Fixed"/> plans (the
    /// calendar-anchored cadence doesn't wait for completion); a <see cref="RecurrenceType.Floating"/>
    /// plan's next due date is left as-is here and only advances at
    /// <see cref="RecordFloatingCompletion"/>.
    /// </summary>
    public void RecordGeneration(Guid occurrenceId)
    {
        if (ActiveOccurrenceId is not null)
        {
            throw new InvalidMaintenancePlanOperationException(
                $"Plan {Id} already has an active occurrence ({ActiveOccurrenceId}); SuppressIfOpen should have prevented this call.");
        }

        ActiveOccurrenceId = occurrenceId;
        if (RecurrenceType == RecurrenceType.Fixed)
        {
            NextDueAtUtc = NextDueAtUtc.AddDays(IntervalDays);
        }
    }

    /// <summary>
    /// The generated Work Order reached Completed, Closed, or Cancelled (docs/01: "A domain event
    /// on the generated Work Order reaching Completed, Closed, or Cancelled clears
    /// active_occurrence_id back to NULL"). Idempotent by construction: only clears if
    /// <paramref name="occurrenceId"/> is still the active one, so calling it again for the same
    /// Work Order's later transitions (e.g. Completed then Closed) is a harmless no-op the second
    /// time.
    /// </summary>
    public void ClearActiveOccurrence(Guid occurrenceId)
    {
        if (ActiveOccurrenceId == occurrenceId)
        {
            ActiveOccurrenceId = null;
        }
    }

    /// <summary>
    /// Floating-only: the generated Work Order actually completed. Recomputes
    /// <see cref="NextDueAtUtc"/> from the real completion time (docs/01: "next due date is
    /// calculated from the actual completion date of the prior occurrence") and clears the active
    /// pointer. Idempotent the same way as <see cref="ClearActiveOccurrence"/>.
    /// </summary>
    public void RecordFloatingCompletion(Guid occurrenceId, DateTimeOffset completedAtUtc)
    {
        if (ActiveOccurrenceId != occurrenceId)
        {
            return;
        }

        if (RecurrenceType == RecurrenceType.Floating)
        {
            NextDueAtUtc = completedAtUtc.AddDays(IntervalDays);
        }

        ActiveOccurrenceId = null;
    }

    private void RequireStatus(MaintenancePlanStatus required, string operation)
    {
        if (Status != required)
        {
            throw new InvalidMaintenancePlanOperationException(
                $"{operation} requires status {required}, but the plan is {Status}.");
        }
    }
}
