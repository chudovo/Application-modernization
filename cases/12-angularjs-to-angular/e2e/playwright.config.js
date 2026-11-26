// @ts-check
const { defineConfig } = require('@playwright/test');

// One test file, two applications. APP=before or APP=after limits a run to one of them.
const apps = {
  before: { port: 4301, command: 'node ../server/server.js --root ../before/app --vendor ../before/node_modules --port 4301' },
  after: { port: 4302, command: 'node ../server/server.js --root ../after/dist/orders/browser --port 4302' },
};
const selected = Object.entries(apps).filter(([name]) => !process.env.APP || process.env.APP === name);

module.exports = defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,   // the tests share one in-memory backend per application
  reporter: [['list']],
  // A German-speaking office in Berlin: the browser's locale and time zone are not the developer's.
  use: { locale: 'de-DE', timezoneId: 'Europe/Berlin', trace: 'off' },
  projects: selected.map(([name, app]) => ({ name, use: { baseURL: `http://127.0.0.1:${app.port}` } })),
  webServer: selected.map(([, app]) => ({ command: app.command, url: `http://127.0.0.1:${app.port}/api/orders`, reuseExistingServer: false })),
});
