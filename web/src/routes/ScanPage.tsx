import { QrCode, TriangleAlert } from 'lucide-react'
import { Navigate, useParams } from 'react-router-dom'
import { getAssetByQrLocator } from '../api/assets'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/useAuth'
import { useAsync } from '../hooks/useAsync'

/**
 * The QR deep-link target (docs/02-security-and-invariants.md § "QR strategy"): a physical tag
 * encodes `/scan/:qrLocator`. This route is deliberately outside <ProtectedRoute> in App.tsx so it
 * can do its own auth check and preserve the scanned URL across a login round-trip — same
 * from-state shape ProtectedRoute/LoginPage already use for return-after-login, so a scan while
 * signed out lands right back here once the technician signs in, no separate query-param scheme.
 *
 * Once authenticated, this asks the *same* RBAC-checked lookup GET /assets/:id would (see
 * GetAssetByQrLocatorAsync's doc comment) — scanning a tag is never a bypass, only a shortcut to
 * the same query keyed by one more field.
 */
export function ScanPage() {
  const { qrLocator } = useParams<{ qrLocator: string }>()
  const { status: authStatus } = useAuth()

  if (authStatus === 'loading') {
    return (
      <div className="flex h-screen items-center justify-center bg-surface">
        <p className="text-sm text-text-secondary">Loading…</p>
      </div>
    )
  }

  if (authStatus === 'unauthenticated') {
    return <Navigate to="/login" state={{ from: { pathname: `/scan/${qrLocator}` } }} replace />
  }

  return <ResolveAsset qrLocator={qrLocator!} />
}

function ResolveAsset({ qrLocator }: { qrLocator: string }) {
  const { status, data, error } = useAsync(() => getAssetByQrLocator(qrLocator), [qrLocator])

  if (status === 'loading') {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 px-6 py-16 text-center">
        <QrCode className="h-6 w-6 text-text-secondary" strokeWidth={1.5} />
        <p className="text-sm text-text-secondary">Looking up this asset…</p>
      </div>
    )
  }

  if (status === 'error') {
    const notFound = error instanceof ApiError && error.status === 404
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 px-6 py-16 text-center">
        {notFound ? (
          <>
            <QrCode className="h-6 w-6 text-text-secondary" strokeWidth={1.5} />
            <h1 className="text-base font-semibold text-text-primary">Tag not recognized</h1>
            <p className="max-w-sm text-sm text-text-secondary">
              This QR tag doesn't match an asset you have access to.
            </p>
          </>
        ) : (
          <>
            <TriangleAlert className="h-6 w-6 text-status-danger" strokeWidth={1.5} />
            <p className="text-sm text-text-primary">
              {error instanceof ApiError ? error.message : 'Could not look up this asset.'}
            </p>
          </>
        )}
      </div>
    )
  }

  return <Navigate to={`/assets/${data.id}`} replace />
}
