/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'

const API_TARGET = process.env.VITE_API_TARGET ?? 'http://localhost:5000'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: API_TARGET, changeOrigin: true },
      '/hubs': { target: API_TARGET, changeOrigin: true, ws: true },
      '/swagger': { target: API_TARGET, changeOrigin: true },
      '/health': { target: API_TARGET, changeOrigin: true },
    },
  },
  build: {
    outDir: 'dist',
    // Still written, for reading a stack trace from the field, but not linked
    // from the bundle, so the browser never fetches 3 MB of maps and the
    // developer tools do not show the source. They are still in dist/, which
    // the Dockerfile copies whole into wwwroot (27 Sep, Open items).
    sourcemap: 'hidden',
    // No hand-made chunk groups. The pages that draw charts are loaded on
    // first use (App.tsx), so the chart library already arrives with the
    // dashboard or a report and never with the login screen or a list. The
    // "vendor" and "charts" groups added on 27 Sep split modules that import
    // each other into chunks that imported each other, and in the production
    // build the dashboard and both report pages crashed on opening ("t is not
    // a function", "re is not a function"); the tests, which run the source,
    // could not see it. Found on the server the same day, and checked since
    // by loading every page's chunk in a real browser.
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
