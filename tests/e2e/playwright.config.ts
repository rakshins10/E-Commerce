import { defineConfig, devices } from '@playwright/test';

/**
 * ONE suite, run TWICE — once against React, once against Angular.
 *
 * The base URL is the only thing that differs, which is what makes this the
 * objective parity proof required by docs/adr/0014. A behavioural difference
 * between the two implementations fails CI, including differences nobody
 * thought to look for.
 *
 * @see tests/e2e/README.md
 */

const target = process.env.E2E_TARGET ?? 'react';
const baseURL =
  process.env.E2E_BASE_URL ?? (target === 'angular' ? 'http://localhost:4200' : 'http://localhost:3000');

export default defineConfig({
  testDir: './specs',
  // Auth specs share a Keycloak SSO session cookie, so running them in parallel
  // makes one test's sign-out race another's sign-in. Serial is slower and
  // correct; a flaky auth suite is worse than a slow one.
  fullyParallel: false,
  workers: 1,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,

  reporter: process.env.CI
    ? [['html', { open: 'never' }], ['github']]
    : [['html', { open: 'never' }], ['list']],

  use: {
    baseURL,
    // Keep the artefacts that make a CI failure diagnosable without a local
    // reproduction - which is the whole difficulty with e2e failures.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },

  projects: [{ name: `${target}-chromium`, use: { ...devices['Desktop Chrome'] } }],

  // Deliberately no `webServer`: the apps run in docker compose alongside
  // Keycloak, which the auth tests need. Starting a dev server here would test
  // a different artefact from the one that ships.
});
