#!/usr/bin/env node
// Native Chrome DevTools check: no test framework or browser package.
import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
import { spawn } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const env = Object.fromEntries(
  readFileSync(join(root, ".env"), "utf8")
    .split("\n")
    .filter((x) => x && !x.startsWith("#"))
    .map((x) => [x.slice(0, x.indexOf("=")), x.slice(x.indexOf("=") + 1)]),
);
const executable =
  process.env.CHROME_BIN ??
  ["/opt/google/chrome/chrome", "/usr/bin/google-chrome", "/usr/bin/chromium"].find(existsSync);
assert(executable, "Set CHROME_BIN to a Chrome/Chromium executable.");
const profile = mkdtempSync(join(tmpdir(), "swiftride-browser-"));
const screenshots = mkdtempSync(join(tmpdir(), "swiftride-screenshots-"));
const browser = spawn(
  executable,
  [
    "--headless=new",
    "--no-sandbox",
    "--disable-dev-shm-usage",
    "--disable-gpu",
    "--no-first-run",
    "--remote-debugging-port=0",
    `--user-data-dir=${profile}`,
    "--window-size=1440,1000",
  ],
  { stdio: "ignore" },
);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
let socket;
let sequence = 0;
const pending = new Map();
const errors = [];
const contexts = new Map();
const call = (method, params = {}, sessionId) =>
  new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`CDP timeout: ${method}`));
    }, 30000);
    pending.set(id, { resolve, reject, timer });
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
async function evaluate(session, expression) {
  const result = await call(
    "Runtime.evaluate",
    { expression, awaitPromise: true, returnByValue: true },
    session,
  );
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
  return result.result.value;
}
async function wait(session, expression, description, timeout = 90000) {
  await call("Page.bringToFront", {}, session);
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    try {
      if (await evaluate(session, expression)) return;
    } catch {
      /* navigation replaces context */
    }
    await delay(350);
  }
  await screenshot(session, "browser-failure");
  throw new Error(
    "Browser timeout: " + description + "\n" + (await evaluate(session, "document.body.innerText")),
  );
}
async function click(session, text) {
  const expression = `Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === ${JSON.stringify(text)} && !b.disabled)`;
  await wait(session, `!!(${expression})`, text);
  await evaluate(session, `(${expression}).click(); true`);
}
async function page() {
  const { browserContextId } = await call("Target.createBrowserContext");
  const { targetId } = await call("Target.createTarget", { browserContextId, url: "about:blank" });
  const { sessionId } = await call("Target.attachToTarget", { targetId, flatten: true });
  await call("Page.enable", {}, sessionId);
  await call("Runtime.enable", {}, sessionId);
  await call("Network.enable", {}, sessionId);
  await call(
    "Emulation.setDeviceMetricsOverride",
    { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false },
    sessionId,
  );
  contexts.set(sessionId, browserContextId);
  return sessionId;
}
async function input(session, selector, value) {
  await wait(session, `!!document.querySelector(${JSON.stringify(selector)})`, selector);
  await evaluate(
    session,
    `(() => {
    const element = document.querySelector(${JSON.stringify(selector)});
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(element, ${JSON.stringify(value)});
    element.dispatchEvent(new Event('input', { bubbles: true }));
  })()`,
  );
}
async function field(session, label, value) {
  const selector = `Array.from(document.querySelectorAll('label')).find(l => l.textContent.trim().startsWith(${JSON.stringify(label)}))?.querySelector('input')`;
  await wait(session, `!!(${selector}) && !(${selector}).disabled`, label);
  await evaluate(
    session,
    `(() => {
    const element = ${selector};
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(element, ${JSON.stringify(value)});
    element.dispatchEvent(new Event('input', { bubbles: true }));
  })()`,
  );
}
async function request(session, path, body, method = "POST") {
  return evaluate(
    session,
    `(async () => {
    const response = await fetch('/api/backend/' + ${JSON.stringify(path)}, {
      method: ${JSON.stringify(body === undefined ? "GET" : method)},
      headers: { 'Content-Type': 'application/json' },
      ${body === undefined ? "" : `body: JSON.stringify(${JSON.stringify(body)}),`}
    });
    return { status: response.status, body: await response.json().catch(() => null) };
  })()`,
  );
}
async function sessionCheck(session, role) {
  await wait(session, `!!document.querySelector('a[href="#history"]')`, "dashboard after OIDC");
  const data = await evaluate(session, `(async () => (await fetch('/api/auth/session')).json())()`);
  assert(data.user.id, "Session must include Keycloak subject.");
  assert.deepEqual(
    data.user.roles.filter((r) => ["user", "driver", "ops"].includes(r)),
    [role],
  );
  assert(
    !JSON.stringify(data).match(/accessToken|refreshToken|access_token|refresh_token/),
    "Browser session must not expose tokens.",
  );
  return data.user.id;
}
async function login(session, username, password, role, newPassword) {
  await call("Page.navigate", { url: "http://localhost:3000" }, session);
  await click(session, "Đăng nhập để tiếp tục ↗");
  await input(session, "#username", username);
  await input(session, "#password", password);
  await evaluate(session, `document.querySelector('#kc-login').click()`);
  if (newPassword) {
    await input(session, "#password-new", newPassword);
    await input(session, "#password-confirm", newPassword);
    await evaluate(
      session,
      `document.querySelector('input[type="submit"],button[type="submit"]').click()`,
    );
  }
  return sessionCheck(session, role);
}
async function geolocation(session, latitude, longitude) {
  await call("Browser.grantPermissions", {
    browserContextId: contexts.get(session),
    origin: "http://localhost:3000",
    permissions: ["geolocation"],
  });
  await call("Emulation.setGeolocationOverride", { latitude, longitude, accuracy: 10 }, session);
}
async function search(session, label, text) {
  await field(session, label, text);
  const selector = `document.querySelector(${JSON.stringify(`ul[aria-label="Kết quả ${label}"] button`)})`;
  await wait(session, `!!(${selector})`, `Geoapify search: ${label}`);
  await evaluate(session, `(${selector}).click()`);
}
async function noAlerts(session, name) {
  const alerts = await evaluate(
    session,
    `Array.from(document.querySelectorAll('[role="alert"]')).map(e => e.textContent.trim())`,
  );
  assert.deepEqual(alerts, [], name + ": visible errors");
}
async function screenshot(session, name) {
  await call("Page.bringToFront", {}, session);
  const image = await call(
    "Page.captureScreenshot",
    { format: "png", captureBeyondViewport: false },
    session,
  );
  writeFileSync(join(screenshots, name + ".png"), Buffer.from(image.data, "base64"));
}
try {
  const portFile = join(profile, "DevToolsActivePort");
  for (let i = 0; i < 100 && !existsSync(portFile); i++) {
    assert(browser.exitCode === null, "Chrome exited");
    await delay(100);
  }
  assert(existsSync(portFile), "Chrome debugging port unavailable");
  const [port, path] = readFileSync(portFile, "utf8").split("\n");
  socket = new WebSocket(`ws://127.0.0.1:${port}${path}`);
  await new Promise((resolve, reject) => {
    socket.addEventListener("open", resolve, { once: true });
    socket.addEventListener("error", reject, { once: true });
  });
  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      const p = pending.get(message.id);
      clearTimeout(p.timer);
      pending.delete(message.id);
      if (message.error) p.reject(new Error(message.error.message));
      else p.resolve(message.result);
    }
    if (message.method === "Runtime.exceptionThrown")
      errors.push(message.params.exceptionDetails.text);
    if (message.method === "Network.responseReceived") {
      const { status, url } = message.params.response;
      const path = new URL(url).pathname;
      if (status >= 500 || (status >= 400 && path.startsWith("/api/backend/screens/")))
        errors.push(`${status} ${path}`);
    }
  });
  const suffix = Date.now().toString(36);
  const password = "Ride9" + randomBytes(12).toString("hex");
  const temporaryPassword = "Temp7" + randomBytes(12).toString("hex");
  const userName = "check-user-" + suffix;
  const driverName = "check-driver-" + suffix;
  const user = await page();
  const ops = await page();
  const driver = await page();

  await call("Page.navigate", { url: "http://localhost:3000" }, user);
  await click(user, "Đăng ký tài khoản User");
  for (const [selector, value] of Object.entries({
    "#firstName": "Kiểm tra",
    "#lastName": "User",
    "#email": userName + "@example.com",
    "#username": userName,
    "#password": password,
    "#password-confirm": password,
  }))
    await input(user, selector, value);
  await evaluate(user, `document.querySelector('input[type="submit"]').click()`);
  const userId = await sessionCheck(user, "user");
  assert.equal((await request(user, "ops/drivers")).status, 403);
  assert.equal((await request(user, "drivers/me")).status, 403);
  console.log(
    "PASS public User registration, isolated role, private tokens, forbidden Ops/Driver APIs",
  );

  await login(ops, "ops1", env.OPS_PASSWORD, "ops");
  await evaluate(ops, `document.querySelector('#operations details').open = true`);
  for (const [label, value] of Object.entries({
    "Tên đăng nhập": driverName,
    "Họ tên": "Kiểm tra Driver",
    Email: driverName + "@example.com",
    "Số điện thoại": "0901234567",
    "Biển số": "59A-12345",
    "Loại xe": "SwiftCar",
  }))
    await field(ops, label, value);
  await click(ops, "Lưu hồ sơ chờ duyệt");
  await wait(
    ops,
    `Array.from(document.querySelectorAll('#operations tr')).some(r => r.textContent.includes(${JSON.stringify(driverName)}))`,
    "pending Driver application",
  );
  await evaluate(
    ops,
    `Array.from(document.querySelectorAll('#operations tr')).find(r => r.textContent.includes(${JSON.stringify(driverName)})).querySelector('button').click()`,
  );
  await field(ops, "Mật khẩu tạm", temporaryPassword);
  await click(ops, "Duyệt và cấp tài khoản");
  await wait(
    ops,
    `Array.from(document.querySelectorAll('#operations tr')).some(r => r.textContent.includes(${JSON.stringify(driverName)}) && r.textContent.includes('Approved'))`,
    "Ops approves Driver and provisions identity",
  );
  const applications = await request(ops, "ops/drivers?page=1");
  assert.equal(applications.status, 200);
  assert(applications.body.total > 0 && applications.body.items.length <= 10);
  assert.equal((await request(ops, "ops/drivers?page=0")).status, 400);
  await screenshot(ops, "ops-approval");
  console.log("PASS Ops Driver application, approval, provisioning and server pagination");

  const driverId = await login(driver, driverName, temporaryPassword, "driver", password);
  assert.notEqual(driverId, userId);
  const emptySimulation = await evaluate(
    driver,
    `(async () => {
    const r = await fetch('/api/backend/simulations/${driverId}');
    return { status: r.status, text: await r.text() };
  })()`,
  );
  assert.equal(emptySimulation.status, 200);
  assert.equal(
    emptySimulation.text,
    "null",
    "A vehicle without a journey must return valid JSON null.",
  );
  assert.equal((await request(driver, "ops/drivers")).status, 403);
  const trackingReads = await evaluate(
    driver,
    `(async () => {
    const statuses = [];
    for (let batch = 0; batch < 8; batch++) {
      statuses.push(...await Promise.all(Array.from({ length: 20 }, async () =>
        (await fetch('/api/backend/drivers/me')).status)));
    }
    return statuses;
  })()`,
  );
  assert(
    trackingReads.every((status) => status === 200),
    "Tracking reads remain within their own budget.",
  );
  await geolocation(driver, 10.772, 106.704);
  await click(driver, "Dùng vị trí hiện tại");
  await click(driver, "Lưu vị trí xe");
  await click(driver, "Bật nhận chuyến");
  await wait(driver, `document.body.innerText.includes('Đang trực tuyến')`, "Driver online");
  console.log("PASS Driver first-login password change, geolocation, saved position, availability");

  await call("Page.bringToFront", {}, user);
  await wait(
    user,
    `document.querySelectorAll('.leaflet-tile-loaded').length > 0`,
    "real map tiles",
  );
  const mapBounds = await evaluate(
    user,
    `(() => { const r = document.querySelector('.leaflet-container').getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })()`,
  );
  for (const type of ["mousePressed", "mouseReleased"])
    await call(
      "Input.dispatchMouseEvent",
      { type, ...mapBounds, button: "left", clickCount: 1 },
      user,
    );
  await wait(
    user,
    `Array.from(document.querySelectorAll('label')).find(l => l.textContent.trim().startsWith('Điểm đón'))?.querySelector('input').value.length > 0`,
    "map click reverse geocodes pickup",
  );
  await geolocation(user, 10.7769, 106.7009);
  await click(user, "Dùng vị trí hiện tại làm điểm đón");
  await search(user, "Điểm đến", "Dinh Độc Lập");
  await wait(
    user,
    `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Tìm xe và xem giá' && !b.disabled)`,
    "real driving route preview",
  );
  await field(user, "Điểm đến", "Dinh Độc Lập thay đổi");
  assert(
    await evaluate(
      user,
      `Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Tìm xe và xem giá').disabled`,
    ),
    "Editing selected address must clear old coordinates.",
  );
  await search(user, "Điểm đến", "Dinh Độc Lập");
  await wait(
    user,
    `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Tìm xe và xem giá' && !b.disabled)`,
    "updated route preview ready",
  );
  await screenshot(user, "user-route");
  await click(user, "Tìm xe và xem giá");
  await wait(
    user,
    `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Đồng ý giá' && !b.disabled)`,
    "quote ready before simulation exists",
  );
  const quoted = (await request(user, "trips")).body.find(
    (t) => t.status === "AwaitingRiderConfirmation",
  );
  assert(quoted, "User receives a quote.");
  for (const [session, name] of [
    [user, "User"],
    [ops, "Ops"],
  ]) {
    const preview = await request(session, "screens/trips/" + quoted.id);
    assert.equal(preview.status, 200, name + " can view trip before simulation starts.");
    assert.equal(preview.body.simulation, null);
  }
  await click(user, "Đồng ý giá");
  await click(driver, "Nhận chuyến");
  await click(driver, "Đang đến đón");
  await click(driver, "Tạm dừng");
  const trips = await request(driver, "trips");
  assert.equal(trips.status, 200);
  const trip = trips.body.find((t) => t.riderId === userId && t.status === "EnRoute");
  assert(trip, "Driver must receive this User trip.");
  const before = await request(driver, "simulations/" + driverId);
  assert.equal(before.body.paused, true);
  await delay(2200);
  const after = await request(driver, "simulations/" + driverId);
  assert.equal(after.body.progress, before.body.progress, "Paused vehicle must not move.");
  assert.equal(
    (await request(user, "simulations/" + driverId, { speed: 20, paused: false }, "PUT")).status,
    403,
  );
  const screen = await request(user, "screens/trips/" + trip.id);
  assert.equal(screen.status, 200);
  assert.equal(
    screen.body.simulation.tripId,
    trip.id,
    "User can track own trip via authorized screen.",
  );
  await evaluate(
    driver,
    `(() => { const select = Array.from(document.querySelectorAll('label')).find(l => l.textContent.trim().startsWith('Tốc độ')).querySelector('select'); select.value = '20'; select.dispatchEvent(new Event('change', { bubbles: true })); })()`,
  );
  await wait(
    driver,
    `(async () => (await (await fetch('/api/backend/simulations/${driverId}')).json()).speed === 20)()`,
    "simulation speed saved",
  );
  await click(driver, "Tiếp tục");
  await click(driver, "Đã đón khách");
  await wait(
    driver,
    `(async () => { const s = await (await fetch('/api/backend/simulations/${driverId}')).json(); return s.stage === 'PickedUp'; })()`,
    "dropoff leg begins",
  );
  await click(driver, "Tạm dừng");
  const onboard = (await request(driver, "trips/" + trip.id)).body;
  assert.equal(
    (await request(driver, "trips/" + trip.id + "/dropoff", { expectedVersion: onboard.version }))
      .status,
    409,
    "Cannot drop off before arrival.",
  );
  const secondLeg = (await request(driver, "simulations/" + driverId)).body;
  assert.equal(secondLeg.speed, 20, "Speed persists between legs.");
  await screenshot(driver, "driver-active");
  await click(driver, "Tiếp tục");
  await click(driver, "Đã trả khách");
  await wait(
    user,
    `document.querySelector('[aria-label="Chi tiết chuyến"]')?.innerText.includes('Đã trả khách') || document.body.innerText.includes('Đặt chuyến mới')`,
    "completed journey",
  );
  await screenshot(user, "user-complete");
  console.log(
    "PASS real map/search/reverse/route, stale address guard, booking, Driver acceptance, simulation controls after tracking reads, arrival enforcement and dropoff",
  );

  await click(user, "Đặt chuyến mới");
  await click(user, "Tìm xe và xem giá");
  await click(user, "Từ chối giá");
  await wait(
    user,
    `document.querySelector('[aria-label="Chi tiết chuyến"]')?.innerText.includes('Đã hủy')`,
    "User cancels quote",
  );
  await field(user, "Họ và tên", "Kiểm tra User");
  await field(user, "Số điện thoại", "0907654321");
  await click(user, "Lưu hồ sơ");
  await wait(
    user,
    `document.querySelector('#profile').innerText.includes('Đã lưu hồ sơ.')`,
    "profile saved",
  );
  assert.equal((await request(user, "profiles/me")).body.phone, "0907654321");
  assert((await request(user, "trips/history?page=1")).body.total >= 2);
  console.log("PASS User cancellation, profile save and paginated history");

  for (const [session, name] of [
    [user, "user-mobile"],
    [driver, "driver-mobile"],
    [ops, "ops-mobile"],
  ]) {
    await call(
      "Emulation.setDeviceMetricsOverride",
      { width: 360, height: 800, deviceScaleFactor: 1, mobile: true },
      session,
    );
    await delay(300);
    assert(
      await evaluate(session, "document.documentElement.scrollWidth <= window.innerWidth"),
      `${name}: horizontal overflow`,
    );
    await call(
      "Input.dispatchKeyEvent",
      { type: "keyDown", key: "Tab", code: "Tab", windowsVirtualKeyCode: 9 },
      session,
    );
    await call(
      "Input.dispatchKeyEvent",
      { type: "keyUp", key: "Tab", code: "Tab", windowsVirtualKeyCode: 9 },
      session,
    );
    assert(
      await evaluate(session, `document.activeElement.tagName !== 'BODY'`),
      `${name}: keyboard focus`,
    );
    await noAlerts(session, name);
    await screenshot(session, name);
  }
  await click(driver, "Chuyển ngoại tuyến");
  console.log("PASS User/Driver/Ops 360px responsive layout, keyboard focus and no visible errors");
  if (process.argv.includes("--refresh")) {
    console.log("Checking server-side access-token refresh after 300-second lifespan...");
    for (let i = 0; i < 31; i++) await delay(10000);
    assert.equal((await request(user, "profiles/me")).status, 200);
    console.log("PASS browser access-token refresh after expiry");
  }
  await click(user, "Đăng xuất");
  await wait(
    user,
    `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Đăng nhập để tiếp tục ↗')`,
    "logout",
  );
  assert.equal((await request(user, "trips")).status, 401);
  console.log("PASS logout clears backend access");
  assert.equal(errors.length, 0, "Browser exceptions / HTTP 5xx: " + errors.join("; "));
  console.log("Browser checks passed. Screenshots:", screenshots);
} finally {
  if (socket?.readyState === WebSocket.OPEN) {
    try {
      await call("Browser.close");
    } catch {}
    socket.close();
  }
  browser.kill("SIGTERM");
  await delay(500);
  rmSync(profile, { recursive: true, force: true, maxRetries: 3 });
}
