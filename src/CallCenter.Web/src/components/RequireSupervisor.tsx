import { Navigate, Outlet } from 'react-router-dom'
import { UserRoles } from '../api/auth'
import { useAuth } from '../auth/context'

/**
 * Wraps the supervisor pages, inside RequireAuth. An agent may sign in to the
 * web app since S-63, for the Agent App download and nothing else, so any
 * other page sends them there, a typed address and a bookmark included.
 *
 * The screens are not the only guard: every supervisor endpoint refuses an
 * agent's token itself (SupervisorOnly). This keeps an agent off pages that
 * would only show failures.
 */
export default function RequireSupervisor() {
  const { user } = useAuth()

  if (user?.role !== UserRoles.Supervisor) {
    return <Navigate to="/agent-app" replace />
  }

  return <Outlet />
}
