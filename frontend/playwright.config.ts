import { defineConfig, devices } from '@playwright/test';
import * as path from 'path';

export default defineConfig({
  testDir: path.resolve(__dirname, 'e2e'),
  testMatch: /.*\.spec\.ts/,
  globalSetup: path.resolve(__dirname, 'e2e/global-setup.ts'),
  fullyParallel: false,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  workers: 1,
  reporter: 'html',
  use: {
    baseURL: process.env['BASE_URL'] || 'http://localhost:4200',
    trace: 'on-first-retry',
  },
  webServer: {
    command: 'node tools/serve-spa.js',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env['CI'],
    cwd: path.resolve(__dirname, '..'),
    timeout: 30000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
