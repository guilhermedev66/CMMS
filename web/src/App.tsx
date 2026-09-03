import { Route, Routes } from 'react-router-dom'
import { EmptyState } from './components/EmptyState'
import { AppShell } from './layout/AppShell'
import { navItems } from './nav'

export default function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        {navItems.map((item) => (
          <Route key={item.path} path={item.path} element={<EmptyState {...item} />} />
        ))}
      </Route>
    </Routes>
  )
}
