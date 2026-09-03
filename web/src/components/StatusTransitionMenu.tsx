import { useState } from 'react'
import { getAvailableActions, runWorkOrderAction, type WorkOrder, type WorkOrderAction } from '../api/workOrders'
import { ReasonDialog } from './ReasonDialog'

interface StatusTransitionMenuProps {
  workOrder: WorkOrder
  onChanged: (updated: WorkOrder) => void
  disabled?: boolean
}

/**
 * The one control that actually moves a Work Order through its lifecycle. Only ever offers
 * actions getAvailableActions() says exist for the current status, so an illegal move is
 * unreachable client-side — the server re-checks the same guard regardless (per
 * src/Cmms.Api/WorkOrdersEndpoints.cs), this is a UX narrowing, not the actual security boundary.
 */
export function StatusTransitionMenu({ workOrder, onChanged, disabled }: StatusTransitionMenuProps) {
  const [pending, setPending] = useState<WorkOrderAction | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const actions = getAvailableActions(workOrder.status)

  async function apply(action: WorkOrderAction, reason?: string) {
    setBusy(true)
    setError(null)
    try {
      const updated = await runWorkOrderAction(workOrder.id, action, reason)
      onChanged(updated)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update this Work Order.')
    } finally {
      setBusy(false)
      setPending(null)
    }
  }

  function handleSelect(command: string) {
    const action = actions.find((a) => a.command === command)
    if (!action) return
    if (action.requiresReason) {
      setPending(action)
      return
    }
    void apply(action)
  }

  if (actions.length === 0) {
    return <p className="text-xs text-text-secondary">No further actions from {workOrder.status}.</p>
  }

  return (
    <div className="flex flex-col gap-1">
      <select
        value=""
        disabled={disabled || busy}
        onChange={(event) => {
          if (event.target.value) handleSelect(event.target.value)
          event.target.value = ''
        }}
        aria-label="Actions…"
        className="rounded-sm border border-border bg-surface-raised px-2.5 py-1.5 text-sm text-text-primary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none disabled:opacity-60"
      >
        <option value="" disabled>
          Actions…
        </option>
        {actions.map((action) => (
          <option key={action.command} value={action.command}>
            {action.command}
          </option>
        ))}
      </select>
      {error && <p className="text-xs text-status-danger">{error}</p>}

      {pending && (
        <ReasonDialog
          commandLabel={pending.command}
          onCancel={() => setPending(null)}
          onConfirm={(reason) => void apply(pending, reason)}
        />
      )}
    </div>
  )
}
