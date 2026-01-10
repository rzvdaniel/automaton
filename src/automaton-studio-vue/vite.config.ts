import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'path'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // GitHub Pages base path (repository name) for production
  const base = mode === 'production' ? '/automaton/' : '/'

  return {
    base,
    plugins: [vue()],
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
    // Optimize Monaco Editor for production build
    optimizeDeps: {
      include: ['monaco-editor'],
    },
  }
})
