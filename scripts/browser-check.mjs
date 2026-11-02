#!/usr/bin/env node
// Native Chrome DevTools check: no test framework or browser package.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const env = Object.fromEntries(readFileSync(join(root, '.env'), 'utf8').split('\n').filter(x => x && !x.startsWith('#')).map(x => [x.slice(0, x.indexOf('=')), x.slice(x.indexOf('=') + 1)]));
const executable = process.env.CHROME_BIN ?? ['/opt/google/chrome/chrome', '/usr/bin/google-chrome', '/usr/bin/chromium'].find(existsSync);
assert(executable, 'Set CHROME_BIN to a Chrome/Chromium executable.');
const profile = mkdtempSync(join(tmpdir(), 'swiftride-browser-'));
const browser = spawn(executable, ['--headless=new', '--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu', '--no-first-run', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--window-size=1440,1000'], { stdio: 'ignore' });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let socket;
let sequence = 0;
const pending = new Map();
const errors = [];
const call = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
  const id = ++sequence;
  const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 30000);
  pending.set(id, { resolve, reject, timer });
  socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
});
async function evaluate(session, expression) {
  const result = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, session);
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
  return result.result.value;
}
async function wait(session, expression, description, timeout = 90000) {
  await call('Page.bringToFront', {}, session);
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    try { if (await evaluate(session, expression)) return; } catch { /* navigation replaces context */ }
    await delay(350);
  }
  await screenshot(session, 'browser-failure');
  throw new Error('Browser timeout: ' + description + '\n' + await evaluate(session, 'document.body.innerText'));
}
async function click(session, text) {
  const expression = `Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === ${JSON.stringify(text)} && !b.disabled)`;
  await wait(session, `!!(${expression})`, text);
  await evaluate(session, `(${expression}).click(); true`);
}
async function page() {
  const { browserContextId } = await call('Target.createBrowserContext');
  const { targetId } = await call('Target.createTarget', { browserContextId, url: 'about:blank' });
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
  await call('Page.enable', {}, sessionId); await call('Runtime.enable', {}, sessionId); await call('Network.enable', {}, sessionId);
  await call('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false }, sessionId);
  return sessionId;
}
async function login(session, user, password) {
  await call('Page.navigate', { url: 'http://localhost:3000' }, session);
  await click(session, 'Đăng nhập để tiếp tục ↗');
  await wait(session, `!!document.querySelector('#username')`, 'Keycloak login');
  await evaluate(session, `document.querySelector('#username').value = ${JSON.stringify(user)}; document.querySelector('#password').value = ${JSON.stringify(password)}; document.querySelector('#kc-login').click(); true`);
  await wait(session, `!!document.querySelector('a[href="#history"]')`, 'dashboard after OIDC');
  const sessionData = await evaluate(session, `(async () => { const r=await fetch('/api/auth/session'); return r.json(); })()`);
  assert(sessionData.user.id && sessionData.user.roles.length, 'Session must include role and identity.');
  assert.equal(sessionData.user.id, user === 'driver1' ? '22222222-2222-2222-2222-000000000001' : '11111111-1111-1111-1111-000000000001', 'Session must use the Keycloak subject, matching backend ownership.');
  assert(!JSON.stringify(sessionData).match(/accessToken|refreshToken|access_token|refresh_token/), 'Browser session must not expose tokens.');
  console.log('PASS browser OIDC and private token session:', user);
}
async function screenshot(session, name) {
  await call('Page.bringToFront', {}, session);
  mkdirSync(join(root, 'artifacts'), { recursive: true });
  const image = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false }, session);
  writeFileSync(join(root, 'artifacts', name + '.png'), Buffer.from(image.data, 'base64'));
}
try {
  const portFile = join(profile, 'DevToolsActivePort');
  for (let i = 0; i < 100 && !existsSync(portFile); i++) { assert(browser.exitCode === null, 'Chrome exited'); await delay(100); }
  assert(existsSync(portFile), 'Chrome debugging port unavailable');
  const [port, path] = readFileSync(portFile, 'utf8').split('\n');
  socket = new WebSocket(`ws://127.0.0.1:${port}${path}`);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      const p = pending.get(message.id); clearTimeout(p.timer); pending.delete(message.id);
      if (message.error) p.reject(new Error(message.error.message)); else p.resolve(message.result);
    }
    if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.text);
    if (message.method === 'Network.responseReceived' && message.params.response.status >= 500)
      errors.push(`${message.params.response.status} ${new URL(message.params.response.url).pathname}`);
  });
  const driver = await page(); const rider = await page();
  await login(driver, 'driver1', env.DRIVER_PASSWORD);
  await wait(driver, `Array.from(document.querySelectorAll('button')).some(b => ['Bật nhận chuyến', 'Chuyển ngoại tuyến'].includes(b.textContent.trim()) && !b.disabled)`, 'driver availability loaded');
  if (await evaluate(driver, `Array.from(document.querySelectorAll('button')).some(b=>b.textContent.trim()==='Bật nhận chuyến')`)) await click(driver, 'Bật nhận chuyến');
  await wait(driver, `document.body.innerText.includes('Đang trực tuyến')`, 'driver online');
  await login(rider, 'rider1', env.RIDER_PASSWORD);
  await screenshot(rider, 'rider-desktop');
  await click(rider, 'Tìm xe và xem giá');
  await click(rider, 'Đồng ý giá');
  await click(driver, 'Nhận chuyến');
  await click(driver, 'Đang đến đón');
  await click(driver, 'Đã đón khách');
  await screenshot(driver, 'driver-active');
  await click(driver, 'Đã trả khách');
  await wait(rider, `document.body.innerText.includes('Đã thu tiền')`, 'settled payment on Rider screen');
  await screenshot(rider, 'rider-paid');
  console.log('PASS browser Rider/Driver booking, quote, accept, en-route, pickup, dropoff, payment');
  await click(rider, 'Tìm xe và xem giá');
  await click(rider, 'Từ chối giá');
  await wait(rider, `document.querySelector('[aria-label="Chi tiết chuyến"]')?.innerText.includes('Đã hủy')`, 'rider rejects quote');
  console.log('PASS browser rider cancellation');
  for (const [session, name] of [[rider, 'rider-mobile'], [driver, 'driver-mobile']]) {
    await call('Emulation.setDeviceMetricsOverride', { width: 360, height: 800, deviceScaleFactor: 1, mobile: true }, session);
    await delay(300);
    assert(await evaluate(session, 'document.documentElement.scrollWidth <= window.innerWidth'), `${name}: horizontal overflow`);
    await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, session);
    await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, session);
    assert(await evaluate(session, `document.activeElement.tagName !== 'BODY'`), `${name}: keyboard focus`);
    await screenshot(session, name);
  }
  console.log('PASS browser 360px responsive layout and keyboard focus');
  if (process.argv.includes('--refresh')) {
    console.log('Checking access-token refresh after its 300-second lifespan...');
    for (let i = 0; i < 31; i++) await delay(10000);
    const result = await evaluate(rider, `(async () => {
      const response = await fetch('/api/backend/wallets/me');
      return { status: response.status, body: await response.json() };
    })()`);
    assert.equal(result.status, 200, 'Expired access token must refresh server-side.');
    assert.equal(result.body.currency, 'VND');
    console.log('PASS browser access-token refresh after expiry');
  }
  await click(rider, 'Đăng xuất');
  await wait(rider, `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Đăng nhập để tiếp tục ↗')`, 'logout');
  assert.equal(await evaluate(rider, `(async () => (await fetch('/api/backend/trips')).status)()`), 401);
  console.log('PASS browser logout clears backend access');
  assert.equal(errors.length, 0, 'Browser exceptions / HTTP 5xx: ' + errors.join('; '));
  console.log('Browser checks passed. Screenshots: artifacts/*.png');
} finally {
  if (socket?.readyState === WebSocket.OPEN) { try { await call('Browser.close'); } catch {} socket.close(); }
  browser.kill('SIGTERM');
  await delay(500);
  rmSync(profile, { recursive: true, force: true, maxRetries: 3 });
}
