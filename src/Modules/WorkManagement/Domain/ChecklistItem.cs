namespace Cmms.Modules.WorkManagement.Domain;

/// <summary>
/// Per docs/01-domain-and-workflows.md § "Checklist item types". SCOPE CUT: no separate reusable
/// "checklist template" entity in this slice — docs/01 calls the definition "a versioned template"
/// snapshotted onto the Work Order at creation, but building template CRUD/versioning is out of
/// bounds for this pass; items are defined directly on the Work Order instead (still effectively
/// snapshotted, since they belong to this one Work Order and are never shared). Revisit if template
/// reuse across many Work Orders becomes a real requirement.
/// </summary>
public enum ChecklistItemType
{
    Boolean,
    Numeric,
    SingleSelect,
    PhotoRequired,
    Note
}

public sealed class InvalidChecklistItemOperationException(string message) : InvalidOperationException(message);

/// <summary>
/// One item on a Work Order's checklist, execution-cycle-scoped (docs/01: "All execution-scoped
/// child data ... is keyed by (work_order_id, execution_cycle)"). Definition fields are set at
/// creation and never change; response fields are set once by <see cref="Resolve"/> and are
/// re-editable only by resolving again (no separate "unresolve" — matches this codebase's general
/// pattern of explicit, audited corrections rather than silent edits).
/// </summary>
public sealed class ChecklistItem
{
    private ChecklistItem()
    {
    }

    public ChecklistItem(
        Guid workOrderId,
        Guid siteId,
        int executionCycle,
        int sortOrder,
        ChecklistItemType itemType,
        string label,
        bool isRequired,
        bool safetyCritical = false,
        decimal? numericMinValue = null,
        decimal? numericMaxValue = null,
        string? numericUnit = null,
        string? singleSelectOptionsCsv = null)
    {
        if (safetyCritical && itemType != ChecklistItemType.Boolean)
        {
            throw new ArgumentException("safety_critical only applies to Boolean items.", nameof(safetyCritical));
        }

        Id = Guid.CreateVersion7();
        WorkOrderId = workOrderId;
        SiteId = siteId;
        ExecutionCycle = executionCycle;
        SortOrder = sortOrder;
        ItemType = itemType;
        Label = label.Trim();
        IsRequired = isRequired;
        SafetyCritical = safetyCritical;
        NumericMinValue = numericMinValue;
        NumericMaxValue = numericMaxValue;
        NumericUnit = numericUnit;
        SingleSelectOptionsCsv = singleSelectOptionsCsv;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid SiteId { get; private set; }

    public int ExecutionCycle { get; private set; }

    public int SortOrder { get; private set; }

    public ChecklistItemType ItemType { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public bool IsRequired { get; private set; }

    /// <summary>Boolean items only — covers LOTO-style sign-off without a separate item type.</summary>
    public bool SafetyCritical { get; private set; }

    public decimal? NumericMinValue { get; private set; }

    public decimal? NumericMaxValue { get; private set; }

    public string? NumericUnit { get; private set; }

    /// <summary>Comma-separated option labels for <see cref="ChecklistItemType.SingleSelect"/>.</summary>
    public string? SingleSelectOptionsCsv { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    // ---------- Response ----------

    public bool IsResolved { get; private set; }

    public bool? BooleanValue { get; private set; }

    public decimal? NumericValue { get; private set; }

    public string? SelectedOption { get; private set; }

    public string? NoteText { get; private set; }

    /// <summary>Set for <see cref="ChecklistItemType.PhotoRequired"/> — points at an Attachment in
    /// the Attachments module. No FK (schema-per-module boundary, same as every other cross-module
    /// reference in this codebase); only an <c>Active</c> attachment linked here satisfies the
    /// completion guard (docs/02: "a Pending one does not").</summary>
    public Guid? AttachmentId { get; private set; }

    /// <summary>Amber/red flag when a Numeric value falls outside its tolerance band. Null for
    /// non-Numeric items or an unresolved Numeric item.</summary>
    public bool? NumericOutOfTolerance { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>
    /// Resolves this item per its <see cref="ItemType"/>. Only the value matching the item's type
    /// is accepted — passing e.g. a boolean value for a Numeric item is a caller bug, not a
    /// legitimate "clear and reset" operation, so it throws rather than silently accepting garbage.
    /// </summary>
    public void Resolve(
        Guid resolvedByUserId,
        bool? booleanValue,
        decimal? numericValue,
        string? selectedOption,
        string? noteText,
        Guid? attachmentId)
    {
        switch (ItemType)
        {
            case ChecklistItemType.Boolean:
                if (booleanValue is null)
                {
                    throw new InvalidChecklistItemOperationException("A Boolean item requires a boolean value.");
                }

                BooleanValue = booleanValue;
                break;

            case ChecklistItemType.Numeric:
                if (numericValue is null)
                {
                    throw new InvalidChecklistItemOperationException("A Numeric item requires a numeric value.");
                }

                NumericValue = numericValue;
                NumericOutOfTolerance =
                    (NumericMinValue is not null && numericValue < NumericMinValue) ||
                    (NumericMaxValue is not null && numericValue > NumericMaxValue);
                break;

            case ChecklistItemType.SingleSelect:
                if (string.IsNullOrWhiteSpace(selectedOption))
                {
                    throw new InvalidChecklistItemOperationException("A SingleSelect item requires a selected option.");
                }

                var options = (SingleSelectOptionsCsv ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (!options.Contains(selectedOption))
                {
                    throw new InvalidChecklistItemOperationException($"\"{selectedOption}\" is not one of this item's options.");
                }

                SelectedOption = selectedOption;
                break;

            case ChecklistItemType.PhotoRequired:
                if (attachmentId is null)
                {
                    throw new InvalidChecklistItemOperationException("A PhotoRequired item requires a linked attachment.");
                }

                AttachmentId = attachmentId;
                break;

            case ChecklistItemType.Note:
                if (string.IsNullOrWhiteSpace(noteText))
                {
                    throw new InvalidChecklistItemOperationException("A Note item requires text.");
                }

                NoteText = noteText.Trim();
                break;

            default:
                throw new InvalidChecklistItemOperationException($"Unknown item type {ItemType}.");
        }

        IsResolved = true;
        ResolvedAtUtc = DateTimeOffset.UtcNow;
        ResolvedByUserId = resolvedByUserId;
    }
}
