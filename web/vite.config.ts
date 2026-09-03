import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
// `defineConfig` from 'vitest/config' re-exports Vite's, merged with the
// `test` block's types — avoids a separate vitest.config.ts.
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      // The backend has no CORS policy by design (docs/03 ADR-15: same-origin
      // cookies, no browser-held bearer token). Proxying keeps the browser's
      // view same-origin in dev too, instead of adding CORS just for this —
      // production needs an equivalent same-origin reverse proxy at deploy time (M6).
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: true,
  },
})
