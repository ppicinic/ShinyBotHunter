import { defineConfig } from '@playwright/test'

export default defineConfig({
  testMatch: '*.spec.ts',
  workers: 1,
  retries: 0,
  timeout: 30 * 60 * 1000,
  reporter: 'list',
  outputDir: 'qa-runs/playwright-output'
})
