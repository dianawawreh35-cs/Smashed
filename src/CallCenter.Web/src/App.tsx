import { Navigate, Route, Routes } from 'react-router-dom'
import LoginPage from './pages/LoginPage'
import CallsPage from './pages/CallsPage'
import ApplicationsPage from './pages/ApplicationsPage'
import ApplicationReportsPage from './pages/ApplicationReportsPage'
import DashboardPage from './pages/DashboardPage'
import UsersPage from './pages/UsersPage'
import SettingsPage from './pages/SettingsPage'
import ContactsPage from './pages/ContactsPage'
import DeliveryPage from './pages/DeliveryPage'
import MenuPage from './pages/MenuPage'
import ClassificationPage from './pages/ClassificationPage'
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
          <Route path="/calls" element={<CallsPage />} />
          <Route path="/applications" element={<ApplicationsPage />} />
          <Route path="/application-reports" element={<ApplicationReportsPage />} />
          <Route path="/contacts" element={<ContactsPage />} />
          <Route path="/delivery" element={<DeliveryPage />} />
          <Route path="/menu" element={<MenuPage />} />
          <Route path="/classification" element={<ClassificationPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}
