import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, extname } from 'node:path';
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const { chromium } = require(process.argv[2] || 'playwright');
const root = fileURLToPath(new URL('../', import.meta.url));
const { version } = JSON.parse(await readFile(resolve(root, 'package.json'), 'utf8'));
const server = createServer(async (req, res) => {
  const name = (req.url || '/').slice(1);
  if (!['popup.html', 'popup.css', 'popup.js', 'client.js'].includes(name)) { res.writeHead(404).end(); return; }
  try {
    const type = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript' }[extname(name)];
    res.writeHead(200, { 'Content-Type': type }); res.end(await readFile(resolve(root, 'plugin', name)));
  } catch { res.writeHead(500).end(); }
});
await new Promise(done => server.listen(0, '127.0.0.1', done));
let browser;
try {
  const extensionMode = process.argv.includes('--extension');
  let clientId = 'b'.repeat(32);
  let page;
  if (extensionMode) {
    const extensionPath = resolve(root, 'plugin');
    browser = await chromium.launchPersistentContext(resolve(root, 'test-output', 'extension-profile'), {
      headless: true, viewport: { width: 400, height: 600 },
      ...(process.env.BINDVPN_BROWSER ? { executablePath: process.env.BINDVPN_BROWSER } : {}),
      args: [`--disable-extensions-except=${extensionPath}`, `--load-extension=${extensionPath}`]
    });
    page = await browser.newPage();
    await page.goto('chrome://extensions/');
    const extensions = await page.evaluate(() => new Promise(done => chrome.developerPrivate.getExtensionsInfo({ includeDisabled: true, includeTerminated: true }, done)));
    const extension = extensions.find(e => e.name === 'Hayase BindVPN');
    assert.ok(extension, 'The real Chrome extension must load.');
    assert.equal(extension.version, version, 'The loaded extension version must match the release.');
    clientId = extension.id;
  } else {
    browser = await chromium.launch({ headless: true, ...(process.env.BINDVPN_BROWSER ? { executablePath: process.env.BINDVPN_BROWSER } : {}) });
    page = await browser.newPage({ viewport: { width: 400, height: 600 } });
    await page.addInitScript(() => { globalThis.chrome = { ...(globalThis.chrome || {}), runtime: { id: 'b'.repeat(32) } }; });
  }
  const failures = [];
  page.on('pageerror', error => failures.push(error.message));
  let state = {
    version, executable: 'D:\\Apps\\Hayase\\Hayase.exe', adapterId: '', appAvailable: true, testRunning: false,
    enabled: false, blocked: false, ready: false, state: 'unbound', message: 'Binding is off',
    adapters: [
      { id: 'mullvad', name: 'Mullvad', description: 'WireGuard Tunnel', up: true, suggested: true, addresses: ['10.64.0.2'] },
      { id: 'ethernet', name: 'Ethernet', description: 'Intel Ethernet', up: true, suggested: false, addresses: ['192.168.1.20'] },
      { id: 'proton', name: 'ProtonVPN', description: 'WireGuard Tunnel', up: true, suggested: false, addresses: ['10.2.0.2'] }
    ]
  };
  let offline = false;
  const requests = [];
  const observedOrigins = new Set();
  await page.route('http://127.0.0.1:49736/**', async route => {
    const request = route.request();
    assert.equal(request.headers()['x-hayase-bindvpn-client'], clientId);
    observedOrigins.add(request.headers().origin || 'omitted');
    if (offline) { await route.abort(); return; }
    requests.push(request.url());
    if (request.url().endsWith('/bind')) {
      const body = JSON.parse(request.postData());
      state = { ...state, adapterId: body.adapterId, enabled: true, blocked: true, ready: true, state: 'bound', message: 'Restricted to the selected interface' };
    }
    if (request.url().endsWith('/unbind')) state = { ...state, enabled: false, blocked: false, ready: false, state: 'unbound', message: 'Binding is off' };
    const body = request.url().endsWith('/test') ? { passed: true, message: 'Protection test passed.', checks: [{ name: 'Selected interface', state: 'passed', detail: 'TCP connected.' }, { name: 'Fallback blocked', state: 'passed', detail: 'TCP denied.' }] } : state;
    await route.fulfill({ status: 200, contentType: 'application/json', headers: { 'Access-Control-Allow-Origin': '*' }, body: JSON.stringify(body) });
  });
  const url = extensionMode ? `chrome-extension://${clientId}/popup.html` : `http://127.0.0.1:${server.address().port}/popup.html`;
  // Clear the isolated fixture's saved code before page scripts start. Clearing
  // after navigation can race with an in-flight automatic pairing response.
  await page.addInitScript(() => localStorage.clear());
  await page.goto(url);
  if (extensionMode) {
    assert.equal(await page.evaluate(() => chrome.runtime.id), clientId);
  }
  await page.locator('#pair-panel').waitFor({ state: 'visible' });
  await mkdir(resolve(root, 'test-output'), { recursive: true });
  await page.screenshot({ path: resolve(root, 'test-output', 'popup-pair.png') });
  await page.locator('#pair-code').fill('a'.repeat(64));
  await page.locator('#pair').click();
  await page.locator('#controls').waitFor({ state: 'visible' });
  assert.equal(await page.locator('#adapter option').count(), 4);
  // An alternate provider can be selected without binding until the button is pressed.
  await page.locator('#adapter').selectOption('proton');
  assert.equal(requests.filter(u => u.endsWith('/bind')).length, 0);
  await page.locator('#binding-toggle').check();
  await page.waitForFunction(() => document.querySelector('#state').textContent === 'Bound to interface');
  assert.equal(state.adapterId, 'proton');
  await page.screenshot({ path: resolve(root, 'test-output', 'popup-bound.png'), fullPage: true });
  await page.locator('#test').click();
  await page.waitForFunction(() => document.querySelector('#test-summary').textContent === 'Protection test passed.');
  assert.equal(await page.locator('#test-checks li').count(), 2);
  assert.equal(state.enabled, true, 'Testing must not turn binding off.');
  state = { ...state, ready: false, state: 'blocked', message: 'Hayase is blocked until the selected VPN interface is available', adapters: state.adapters.filter(a => a.id !== 'proton') };
  await page.locator('#refresh').click();
  await page.waitForFunction(() => document.querySelector('#state').textContent === 'Hayase is blocked');
  assert.equal(await page.locator('#adapter').inputValue(), 'proton');
  assert.equal(await page.locator('#test').isEnabled(), false);
  assert.equal(await page.locator('#binding-toggle').isEnabled(), true, 'Turning off must remain available when the adapter is missing.');
  await page.screenshot({ path: resolve(root, 'test-output', 'popup-blocked.png') });
  await page.locator('#binding-toggle').uncheck();
  await page.waitForFunction(() => document.querySelector('#state').textContent === 'Binding is off');
  state = { ...state, appAvailable: false, executable: 'D:\\missing\\Hayase.exe', state: 'app_missing', message: 'Hayase was not found. Select Hayase.exe in the helper.' };
  await page.locator('#refresh').click();
  await page.waitForFunction(() => document.querySelector('#state').textContent === 'Hayase not found');
  assert.equal(await page.locator('#binding-toggle').isEnabled(), false);
  assert.equal(await page.locator('#test').isEnabled(), false);
  assert.equal(await page.locator('#app-warning').isVisible(), true);
  await page.screenshot({ path: resolve(root, 'test-output', 'popup-app-missing.png') });
  offline = true;
  await page.locator('#refresh').click();
  await page.locator('#pair-panel').waitFor({ state: 'visible' });
  assert.equal(await page.locator('#state').textContent(), 'Status unavailable');
  assert.deepEqual(failures, []);
  console.log(`PASS: pairing, multiple VPN providers, binding switch, protection results, missing-adapter recovery, missing-application guard, and unavailable-helper states in the 400 × 600 ${extensionMode ? 'real extension' : 'web preview'} popup.`);
  if (extensionMode) console.log(`Observed extension request Origin: ${[...observedOrigins].join(', ')}. Plugin identity header was verified.`);
} finally {
  await browser?.close();
  await new Promise(done => server.close(done));
}
