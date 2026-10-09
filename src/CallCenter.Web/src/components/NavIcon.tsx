import type { ReactNode } from 'react'

/**
 * The side menu's icons (S-70): plain line drawings in the text's colour,
 * written here rather than fetched or taken from an icon package, so the menu
 * needs nothing from the internet (the server is LAN-only) and adds a few
 * hundred bytes. Each one is drawn on a 20 × 20 grid.
 */
const ICONS: Record<string, ReactNode> = {
  dashboard: <path d="M3 3h6v6H3zM11 3h6v6h-6zM3 11h6v6H3zM11 11h6v6h-6z" />,
  calls: <path d="M5 3h3l1.5 4-2 1.5a10 10 0 0 0 4 4l1.5-2 4 1.5v3a2 2 0 0 1-2 2A14 14 0 0 1 3 5a2 2 0 0 1 2-2z" />,
  callReports: <path d="M4 16V10M10 16V4M16 16v-6M3 17h14" />,
  applications: <path d="M4 5a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H8l-4 3z" />,
  applicationReports: <path d="M3 14l4-5 3 3 6-7M3 17h14" />,
  contacts: (
    <>
      <circle cx="10" cy="7" r="3" />
      <path d="M4 17a6 6 0 0 1 12 0" />
    </>
  ),
  mistakes: <path d="M10 3l8 14H2zM10 8v4M10 14.5v.5" />,
  mistakeReports: <path d="M5 3h7l3 3v11H5zM8 14v-2M10.5 14V9M13 14v-3" />,
  breaks: <path d="M4 8h10v4a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4zM14 9h1.5a2 2 0 0 1 0 4H14M7 3v2M10 3v2" />,
  delivery: (
    <>
      <path d="M2 5h10v9H2zM12 8h3l3 3v3h-6" />
      <circle cx="5.5" cy="15.5" r="1.5" />
      <circle cx="14.5" cy="15.5" r="1.5" />
    </>
  ),
  menu: <path d="M4 9a6 6 0 0 1 12 0zM3 12h14M4 15h12l-1 2H5z" />,
  classification: <path d="M3 3h6l8 8-6 6-8-8zM6.5 6.5h.01" />,
  users: (
    <>
      <circle cx="7.5" cy="6.5" r="2.5" />
      <path d="M2.5 16a5 5 0 0 1 10 0M13 4.2a2.5 2.5 0 0 1 0 4.6M14.5 11.5a5 5 0 0 1 3 4.5" />
    </>
  ),
  settings: (
    <>
      <path d="M3 5h4M11 5h6M3 10h10M17 10h0M3 15h2M9 15h8" />
      <circle cx="9" cy="5" r="2" />
      <circle cx="15" cy="10" r="2" />
      <circle cx="7" cy="15" r="2" />
    </>
  ),
  websites: (
    <>
      <circle cx="10" cy="10" r="7" />
      <path d="M3 10h14M10 3c2.5 2.5 2.5 11.5 0 14M10 3c-2.5 2.5-2.5 11.5 0 14" />
    </>
  ),
  logs: <path d="M5 3h10v14H5zM8 7h4M8 10h4M8 13h2" />,
  agentApp: <path d="M3 4h14v9H3zM1.5 16h17M10 6v4.5M8 8.5l2 2 2-2" />,
}

export default function NavIcon({ name }: { name: string }) {
  return (
    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"
         strokeLinejoin="round" className="h-[18px] w-[18px] shrink-0" aria-hidden="true">
      {ICONS[name]}
    </svg>
  )
}
