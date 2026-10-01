import { lazy } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import LoginPage from './pages/LoginPage'
import CallsPage from './pages/CallsPage'
import ApplicationsPage from './pages/ApplicationsPage'
import UsersPage from './pages/UsersPage'
import SettingsPage from './pages/SettingsPage'
import ContactsPage from './pages/ContactsPage'
import MistakesPage from './pages/MistakesPage'
import DeliveryPage from './pages/DeliveryPage'
import MenuPage from './pages/MenuPage'
import ClassificationPage from './pages/ClassificationPage'
import AgentAppPage from './pages/AgentAppPage'
import LogsPage from './pages/LogsPage'
import AppLayout from './components/AppLayout'
import RequireAuth from './components/RequireAuth'
import RequireSupervisor from './components/RequireSupervisor'

// The pages that draw charts, fetched when first opened: recharts is most of
// the bundle, and the login screen and the lists need none of it. The layout's
// PageBoundary shows a line while one loads.
const DashboardPage = lazy(() => import('./pages/DashboardPage'))
const CallReportsPage = lazy(() => import('./pages/CallReportsPage'))
const ApplicationReportsPage = lazy(() => import('./pages/ApplicationReportsPage'))

/**
 * Route table. Everything except the login screen sits behind RequireAuth, so
 * a new supervisor page is protected by being added here rather than by
 * remembering to guard it (S-01). Agents may sign in too, for the Agent App
 * page alone (S-63): every other page is also behind RequireSupervisor, which
 * sends an agent to it.
 */
export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppLayout />}>
          <Route path="/agent-app" element={<AgentAppPage />} />
          <Route element={<RequireSupervisor />}>
            <Route path="/dashboard" element={<DashboardPage />} />
            <Route path="/calls" element={<CallsPage />} />
            <Route path="/call-reports" element={<CallReportsPage />} />
            <Route path="/applications" element={<ApplicationsPage />} />
            <Route path="/application-reports" element={<ApplicationReportsPage />} />
            <Route path="/contacts" element={<ContactsPage />} />
            <Route path="/mistakes" element={<MistakesPage />} />
            <Route path="/delivery" element={<DeliveryPage />} />
            <Route path="/menu" element={<MenuPage />} />
            <Route path="/classification" element={<ClassificationPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="/logs" element={<LogsPage />} />
          </Route>
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}
