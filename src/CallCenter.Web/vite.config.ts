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
    rolldownOptions: {
      output: {
        codeSplitting: {
          // The chart library in a chunk of its own name. The pages that draw
          // charts are loaded on first use (App.tsx), so this is fetched with
          // the dashboard or a report, never with the login screen or a list.
          groups: [
            // React, the router, the query cache and i18next: needed by every
            // page, and they change far less often than the app's own code.
            {
              name: 'vendor',
              test: /[\\/]node_modules[\\/](react|react-dom|scheduler|react-router|react-router-dom|@tanstack|i18next|react-i18next)[\\/]/,
              includeDependenciesRecursively: false,
            },
            {
              name: 'charts',
              test: /[\\/]node_modules[\\/](recharts|recharts-scale|react-smooth|victory-vendor|d3-[^\\/]+)[\\/]/,
              includeDependenciesRecursively: false,
            },
          ],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
