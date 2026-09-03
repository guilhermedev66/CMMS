import { Route, Routes } from 'react-router-dom'
import { LoginPage } from './auth/LoginPage'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { EmptyState } from './components/EmptyState'
import { AppShell } from './layout/AppShell'
import { navItems } from './nav'
import { AssetDetailPage } from './routes/AssetDetailPage'
import { AssetsListPage } from './routes/AssetsListPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route element={<ProtectedRoute />}>
        <Route element={<AppShell />}>
          <Route path="/assets" element={<AssetsListPage />} />
          <Route path="/assets/:assetId" element={<AssetDetailPage />} />
          {navItems
            .filter((item) => item.path !== '/assets')
            .map((item) => (
              <Route key={item.path} path={item.path} element={<EmptyState {...item} />} />
            ))}
        </Route>
      </Route>
    </Routes>
  )
}
