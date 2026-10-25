import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'path'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // GitHub Pages base path (repository name) for production.
  // Override via VITE_BASE_PATH env var (e.g. '/' for Docker builds).
  const base = process.env.VITE_BASE_PATH ?? (mode === 'production' ? '/automaton/' : '/')

  return {
    base,
    plugins: [vue()],
    define: process.env.VITE_RUNTIME_CONFIG === 'true' ? {
      'import.meta.env.VITE_API_BASE_URL': 'globalThis.__AUTOMATON_CONFIG__.apiBaseUrl',
      'import.meta.env.VITE_GITHUB_AUTH_API_BASE_URL': 'globalThis.__AUTOMATON_CONFIG__.apiBaseUrl',
      'import.meta.env.VITE_PUBLIC_APPS_BASE_URL': 'globalThis.__AUTOMATON_CONFIG__.publicAppsBaseUrl',
    } : {},
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    server: {
      port: 5173,
    },
    build: {
      outDir: 'dist',
      sourcemap: mode !== 'production',
    },
  }
})
