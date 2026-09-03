import { useState } from 'react'

interface ReasonDialogProps {
  commandLabel: string
  onConfirm: (reason: string) => void
  onCancel: () => void
}

/** Free-text reason prompt for any transition that requires one (Cancel, Reopen — see api/workOrders.ts). */
export function ReasonDialog({ commandLabel, onConfirm, onCancel }: ReasonDialogProps) {
  const [text, setText] = useState('')
  const value = text.trim()

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <div role="dialog" aria-modal="true" aria-label={commandLabel} className="w-full max-w-sm rounded-md border border-border bg-surface-raised p-4">
        <h2 className="mb-1 text-sm font-semibold text-text-primary">{commandLabel}</h2>
        <p className="mb-3 text-sm text-text-secondary">A reason is required for this action.</p>

        <textarea
          autoFocus
          value={text}
          onChange={(event) => setText(event.target.value)}
          placeholder="Reason…"
          rows={3}
          className="w-full resize-none rounded-sm border border-border bg-surface px-2.5 py-1.5 text-sm text-text-primary placeholder:text-text-secondary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none"
        />

        <div className="mt-4 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            className="rounded-sm border border-border px-3 py-1.5 text-sm text-text-primary hover:border-border-strong"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={value.length === 0}
            onClick={() => onConfirm(value)}
            className="rounded-sm bg-accent px-3 py-1.5 text-sm font-medium text-accent-contrast disabled:opacity-60"
          >
            Confirm
          </button>
        </div>
      </div>
    </div>
  )
}
