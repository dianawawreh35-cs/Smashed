import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import './i18n'

function renderAt(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('App routing', () => {
  it('renders the dashboard placeholder', () => {
    renderAt('/dashboard')
    expect(screen.getByRole('heading', { name: 'لوحة المتابعة' })).toBeInTheDocument()
  })

  it('renders the login placeholder', () => {
    renderAt('/login')
    expect(screen.getByRole('heading', { name: 'تسجيل الدخول' })).toBeInTheDocument()
  })
})
