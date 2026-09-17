import { Navigate, Route, Routes } from 'react-router-dom'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import UsersPage from './pages/UsersPage'
import SettingsPage from './pages/SettingsPage'
import ContactsPage from './pages/ContactsPage'
import AppLayout from './components/AppLayout'
import RequireAuth from './components/RequireAuth'

/**
 * Route table. Everything except the login screen sits behind RequireAuth, so
 * a new supervisor page is protected by being added here rather than by
 * remembering to guard it (S-01).
 */
export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppLayout />}>
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/contacts" element={<ContactsPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}
