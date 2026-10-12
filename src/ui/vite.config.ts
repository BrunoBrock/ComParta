import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  // Relative paths, because the app serves these files from inside its own window.
  base: './',
  build: {
    outDir: '../ComParta.App/Resources/Raw/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    restoreMocks: true,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      // The entry point and the app's own bridge run only inside the app window.
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**', 'src/test-setup.ts', 'src/main.tsx', 'src/bridge/hostBridge.ts'],
      reporter: ['text-summary', 'json-summary', 'html'],
      // Raise these as tests are added; a pull request that lowers coverage below them fails.
      thresholds: { statements: 80, branches: 75, functions: 80, lines: 80 },
    },
  },
})
