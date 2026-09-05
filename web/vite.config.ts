import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
// `vitest/config`, vite'ın defineConfig'ini `test` alanıyla birlikte dışa aktarır.
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // Geliştirmede CORS ile uğraşmamak için API isteklerini vekil üzerinden geçiriyoruz.
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
  test: {
    // Menü kuralları saf TypeScript'tir ama ekran testleri DOM ister; ikisi de
    // aynı koşuda çalışsın diye ortam jsdom'a alındı.
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
})
