import { defineConfig } from '@playwright/test'

// Read-only smoke checks of a deployed Studio (the test server after each update). Not part of `npm test`.
// Run with `npm run smoke`; see smoke/README.md.
export default defineConfig({
  testDir: './smoke',
  testMatch: '**/*.smoke.ts',
  outputDir: './smoke-results',
  fullyParallel: false,
  workers: 1, // one browser at a time: the test server has little memory
  retries: 0,
  timeout: 60_000,
  reporter: [['list']],
  use: {
    baseURL: process.env.AUTOMATON_TEST_STUDIO_URL,
    headless: true,
    screenshot: 'only-on-failure',
    launchOptions: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE }
      : {},
  },
})
