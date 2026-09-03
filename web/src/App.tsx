import { Route, Routes } from 'react-router-dom'
import { LoginPage } from './auth/LoginPage'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { EmptyState } from './components/EmptyState'
import { AppShell } from './layout/AppShell'
import { navItems } from './nav'
import { AssetDetailPage } from './routes/AssetDetailPage'
import { AssetsListPage } from './routes/AssetsListPage'
import { MaintenancePlansPage } from './routes/MaintenancePlansPage'
import { RequestsListPage } from './routes/RequestsListPage'
import { ScanPage } from './routes/ScanPage'
import { WorkOrderDetailPage } from './routes/WorkOrderDetailPage'
import { WorkOrdersListPage } from './routes/WorkOrdersListPage'

const wiredPaths = new Set(['/assets', '/requests', '/work-orders', '/planning'])

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/scan/:qrLocator" element={<ScanPage />} />

      <Route element={<ProtectedRoute />}>
        <Route element={<AppShell />}>
          <Route path="/assets" element={<AssetsListPage />} />
          <Route path="/assets/:assetId" element={<AssetDetailPage />} />
          <Route path="/requests" element={<RequestsListPage />} />
          <Route path="/work-orders" element={<WorkOrdersListPage />} />
          <Route path="/work-orders/:workOrderId" element={<WorkOrderDetailPage />} />
          <Route path="/planning" element={<MaintenancePlansPage />} />
          {navItems
            .filter((item) => !wiredPaths.has(item.path))
            .map((item) => (
              <Route key={item.path} path={item.path} element={<EmptyState {...item} />} />
            ))}
        </Route>
      </Route>
    </Routes>
  )
}
