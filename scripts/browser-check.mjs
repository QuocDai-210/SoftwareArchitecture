#!/usr/bin/env node
// Native Chrome DevTools check: no test framework or browser package.
import assert from "node:assert/strict";
import { randomBytes, randomUUID } from "node:crypto";
import { spawn, execFileSync } from "node:child_process";
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
const cashKeys = new Map();
const refundKeys = new Map();
const injectFaults = process.argv.includes("--faults");
let faultActive = false;
function compose(...args) {
  return execFileSync("docker", ["compose", ...args], {
    cwd: root,
    encoding: "utf8",
    timeout: 60000,
    stdio: ["ignore", "pipe", "pipe"],
  });
}
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
async function request(session, path, body, method = "POST", key) {
  return evaluate(
    session,
    `(async () => {
    const response = await fetch('/api/backend/' + ${JSON.stringify(path)}, {
      method: ${JSON.stringify(body === undefined ? "GET" : method)},
      headers: { 'Content-Type': 'application/json', ${key ? `'Idempotency-Key': ${JSON.stringify(key)}` : ""} },
      ${body === undefined ? "" : `body: JSON.stringify(${JSON.stringify(body)}),`}
    });
    return { status: response.status, body: await response.json().catch(() => null) };
  })()`,
  );
}
async function duplicateCompletion(trip) {
  const messageId = randomUUID();
  const envelope = {
    messageId,
    conversationId: randomUUID(),
    correlationId: randomUUID(),
    messageType: ["urn:message:SwiftRide.Core:RideCompleted"],
    headers: { SchemaVersion: 1 },
    message: {
      eventId: messageId,
      tripId: trip.id,
      riderId: trip.riderId,
      driverId: trip.driverId,
      amount: trip.quote.amount,
      method: "Cod",
      paymentToken: null,
      correlationId: randomUUID(),
    },
  };
  for (let i = 0; i < 3; i++) {
    if (i === 2) envelope.messageId = randomUUID();
    const response = await fetch(
      "http://localhost:15672/api/exchanges/%2F/SwiftRide.Core%3ARideCompleted/publish",
      {
        method: "POST",
        signal: AbortSignal.timeout(15000),
        headers: {
          "Content-Type": "application/json",
          Authorization:
            "Basic " + Buffer.from(env.RABBIT_USER + ":" + env.RABBIT_PASSWORD).toString("base64"),
        },
        body: JSON.stringify({
          properties: { content_type: "application/vnd.masstransit+json", delivery_mode: 2 },
          routing_key: "",
          payload: JSON.stringify(envelope),
          payload_encoding: "string",
        }),
      },
    );
    assert(
      response.ok && (await response.json()).routed,
      "Duplicate completion routed through RabbitMQ",
    );
  }
  for (let i = 0; i < 40; i++) {
    const count = compose(
      "exec",
      "-T",
      "postgres",
      "psql",
      "-U",
      "payment_app",
      "-d",
      "PayDB",
      "-tAc",
      `SELECT COUNT(*) FROM "InboxState" WHERE "MessageId" IN ('${messageId}','${envelope.messageId}') AND "Consumed" IS NOT NULL;`,
    );
    if (Number(count.trim()) === 2) return;
    await delay(500);
  }
  throw new Error("Duplicate completion not consumed by Payment Inbox");
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
    if (message.method === "Network.requestWillBeSent") {
      const { url, headers } = message.params.request;
      if (url.includes("/confirm-cash"))
        cashKeys.set(
          new URL(url).pathname.split("/").at(-2),
          headers["Idempotency-Key"] ?? headers["idempotency-key"],
        );
    }
    if (
      message.method === "Network.requestWillBeSent" &&
      message.params.request.url.includes("/refund")
    ) {
      const { url, headers } = message.params.request;
      refundKeys.set(
        new URL(url).pathname.split("/").at(-2),
        headers["Idempotency-Key"] ?? headers["idempotency-key"],
      );
    }
    if (message.method === "Runtime.exceptionThrown")
      errors.push(message.params.exceptionDetails.text);
    if (message.method === "Network.responseReceived") {
      const { status, url } = message.params.response;
      const path = new URL(url).pathname;
      if (
        !faultActive &&
        (status >= 500 || (status >= 400 && path.startsWith("/api/backend/screens/")))
      )
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
  await wait(
    user,
    `document.readyState === 'complete' && Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Đăng ký tài khoản User')`,
    "landing rendered before screenshot",
  );
  await screenshot(user, "welcome-mint");
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
  assert(
    await evaluate(user, `!!document.querySelector('link[href*="swiftride.css"]')`),
    "Keycloak uses SwiftRide mint theme",
  );
  await evaluate(user, `document.querySelector('input[type="submit"]').click()`);
  const userId = await sessionCheck(user, "user");
  assert.equal((await request(user, "ops/drivers")).status, 403);
  assert.equal((await request(user, "drivers/me")).status, 403);
  console.log(
    "PASS public User registration, isolated role, private tokens, forbidden Ops/Driver APIs",
  );

  await login(ops, "ops1", env.OPS_PASSWORD, "ops");
  await evaluate(ops, `window.location.hash = 'operations'`);
  await wait(ops, `!!document.querySelector('#operations details')`, "Driver management view");
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
  await wait(driver, `!!document.querySelector('.simulation-panel')`, "simulation controls");
  await evaluate(driver, `document.querySelector('.simulation-panel').open = true`);
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
  if (injectFaults) {
    await wait(
      driver,
      `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Đã trả khách' && !b.disabled)`,
      "arrival before broker outage",
    );
    faultActive = true;
    compose("stop", "rabbitmq");
  }
  await click(driver, "Đã trả khách");
  await wait(
    driver,
    `(async () => (await (await fetch('/api/backend/trips/${trip.id}')).json()).status === 'PaymentPending')()`,
    "dropoff committed before inspecting Outbox",
  );
  if (injectFaults) {
    assert.equal((await request(driver, "trips/" + trip.id)).body.status, "PaymentPending");
    const queued = compose(
      "exec",
      "-T",
      "postgres",
      "psql",
      "-U",
      "trip",
      "-d",
      "TripDB",
      "-tAc",
      'SELECT COUNT(*) FROM "OutboxMessage";',
    );
    assert(
      Number(queued.trim()) > 0,
      "Trip PostgreSQL Outbox retains RideCompleted while RabbitMQ is stopped",
    );
    compose("restart", "trip");
    compose("start", "rabbitmq");
    await wait(
      driver,
      `(async () => (await fetch('/api/backend/trips/${trip.id}')).status === 200)()`,
      "Trip restored from PostgreSQL",
    );
  }
  await wait(
    driver,
    `(async () => (await (await fetch('/api/backend/payments/${trip.id}')).json()).status === 'Pending')()`,
    "cash payment created",
  );
  const otherRegistration = await request(ops, "ops/drivers", {
    username: "check-otherdriver-" + suffix,
    name: "Driver khác",
    email: "other-" + suffix + "@example.com",
    phone: "0901111222",
    plate: "59A-54321",
    vehicle: "SwiftRide",
  });
  assert([200, 201].includes(otherRegistration.status));
  assert.equal(
    (
      await request(ops, "ops/drivers/" + otherRegistration.body.id + "/approve", {
        temporaryPassword,
      })
    ).status,
    200,
  );
  const otherDriver = await page();
  await login(otherDriver, "check-otherdriver-" + suffix, temporaryPassword, "driver", password);
  assert.equal((await request(otherDriver, "payments/" + trip.id)).status, 403);
  assert.equal(
    (
      await request(
        otherDriver,
        "payments/" + trip.id + "/confirm-cash",
        { received: true },
        "POST",
        "other-driver-cash",
      )
    ).status,
    403,
  );
  assert.equal((await request(otherDriver, "trips/" + trip.id)).status, 403);
  assert.equal((await request(user, "drivers/" + driverId + "/contact")).status, 403);
  assert.equal(
    (
      await request(
        user,
        "trips",
        { pickup: trip.pickup, destination: trip.destination, method: "Card", paymentToken: null },
        "POST",
        "legacy-method-rejected",
      )
    ).status,
    400,
  );
  assert.equal(
    (
      await request(
        user,
        "trips",
        { pickup: trip.pickup, destination: trip.destination, method: "Cod", paymentToken: null },
        "POST",
        "pending-cash-blocks-booking",
      )
    ).status,
    409,
  );
  const pendingCash = await request(driver, "payments/" + trip.id);
  assert.equal(pendingCash.body.method, "Cod");
  assert.equal(pendingCash.body.collectedAt, null);
  assert.equal(
    (await request(driver, "drivers/me")).body.busy,
    true,
    "Driver stays busy before cash collection",
  );
  assert.equal((await request(driver, "payments/" + trip.id + "/ledger")).body.length, 0);
  assert.equal(
    (
      await request(
        user,
        "payments/" + trip.id + "/confirm-cash",
        { received: true },
        "POST",
        "forbidden-cash",
      )
    ).status,
    403,
  );
  assert.equal(
    (
      await request(
        driver,
        "payments/" + trip.id + "/confirm-cash",
        { received: false },
        "POST",
        "invalid-cash",
      )
    ).status,
    400,
  );
  await wait(
    driver,
    `!!document.querySelector('.cash-receipt input[type="checkbox"]')`,
    "cash confirmation checkbox",
  );
  await evaluate(driver, `document.querySelector('.cash-receipt input[type="checkbox"]').click()`);
  if (injectFaults) compose("stop", "rabbitmq");
  await click(driver, "Xác nhận đã thu tiền");
  if (injectFaults) {
    await wait(
      driver,
      `(async () => (await (await fetch('/api/backend/payments/${trip.id}')).json()).status === 'Settled')()`,
      "cash stored during broker outage",
    );
    assert.equal(
      (await request(driver, "trips/" + trip.id)).body.status,
      "PaymentPending",
      "Trip waits for persisted settlement event",
    );
    const queued = compose(
      "exec",
      "-T",
      "postgres",
      "psql",
      "-U",
      "payment_app",
      "-d",
      "PayDB",
      "-tAc",
      'SELECT COUNT(*) FROM "OutboxMessage";',
    );
    assert(Number(queued.trim()) > 0, "PostgreSQL Outbox retains cash settlement");
    compose("restart", "payment");
    compose("start", "rabbitmq");
  }
  await wait(
    user,
    `document.body.innerText.includes('Đặt chuyến mới')`,
    "COD settled and trip closed",
  );
  const settled = (await request(user, "payments/" + trip.id)).body;
  assert.equal(settled.status, "Settled");
  assert.equal(settled.collectedBy, driverId);
  assert(settled.collectedAt);
  const key = cashKeys.get(trip.id);
  assert(key, "Capture idempotency key from real UI request");
  assert.equal(
    (
      await request(
        driver,
        "payments/" + trip.id + "/confirm-cash",
        { received: true },
        "POST",
        key,
      )
    ).status,
    200,
  );
  assert.equal(
    (
      await request(
        driver,
        "payments/" + trip.id + "/confirm-cash",
        { received: true, reason: "changed" },
        "POST",
        key,
      )
    ).status,
    409,
  );
  const concurrentReplays = await Promise.all(
    Array.from({ length: 3 }, () =>
      request(driver, "payments/" + trip.id + "/confirm-cash", { received: true }, "POST", key),
    ),
  );
  assert(concurrentReplays.every((result) => result.status === 200));
  await duplicateCompletion(trip);
  const ledger = (await request(user, "payments/" + trip.id + "/ledger")).body;
  assert.equal(ledger.length, 1, "One cash charge despite replay");
  assert.equal(ledger[0].lines[0].account, "CashHeldByDriver:" + driverId);
  assert.equal((await request(user, "trips/" + trip.id)).body.status, "Paid");
  await wait(
    driver,
    `(async () => !(await (await fetch('/api/backend/drivers/me')).json()).busy)()`,
    "Driver released after settlement",
  );
  faultActive = false;
  for (const session of [user, driver, ops]) {
    if (
      await evaluate(
        session,
        `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Tải lại')`,
      )
    )
      await click(session, "Tải lại");
  }
  if (injectFaults)
    console.log("PASS RabbitMQ outages, Trip/Payment restarts and both durable Outboxes");
  await screenshot(user, "user-complete");
  console.log(
    "PASS map booking, simulation, arrival enforcement, COD pending, physical collection, ownership and idempotent balanced ledger",
  );

  await evaluate(ops, `window.location.hash = 'payments'`);
  await wait(
    ops,
    `document.querySelector('#payments')?.innerText.includes('${trip.id.slice(0, 8)}')`,
    "Ops cash history",
  );
  await click(ops, "Hoàn tiền");
  await field(ops, "Lý do xử lý", "Hoàn tiền mặt theo kiểm thử");
  await evaluate(
    ops,
    `document.querySelector('[aria-label="Xác nhận giao dịch tiền mặt"] input[type="checkbox"]').click()`,
  );
  await click(ops, "Ghi nhận đã hoàn tiền");
  await wait(
    ops,
    `(async () => (await (await fetch('/api/backend/payments/${trip.id}')).json()).status === 'Refunded')()`,
    "cash refund recorded",
  );
  const refunded = (await request(user, "payments/" + trip.id)).body;
  assert.equal(refunded.status, "Refunded");
  assert(refunded.refundedAt && refunded.refundedBy);
  const refundKey = refundKeys.get(trip.id);
  assert(refundKey);
  const refundPayload = { reason: "Hoàn tiền mặt theo kiểm thử", cashReturned: true };
  const refundReplays = await Promise.all(
    Array.from({ length: 3 }, () =>
      request(ops, "payments/" + trip.id + "/refund", refundPayload, "POST", refundKey),
    ),
  );
  assert(refundReplays.every((result) => result.status === 200));
  assert.equal(
    (
      await request(
        ops,
        "payments/" + trip.id + "/refund",
        { ...refundPayload, reason: "Đổi lý do" },
        "POST",
        refundKey,
      )
    ).status,
    409,
  );
  assert.equal(
    (
      await request(
        ops,
        "payments/" + trip.id + "/refund",
        { ...refundPayload, cashReturned: false },
        "POST",
        refundKey,
      )
    ).status,
    400,
  );
  const driverStats = (await request(driver, "payments/statistics")).body;
  assert.equal(
    driverStats.find((stat) => stat.method === "Cod" && stat.status === "Refunded").amount,
    trip.quote.amount,
  );
  const refundLedger = (await request(user, "payments/" + trip.id + "/ledger")).body;
  assert.equal(refundLedger.length, 2, "Cash refund writes exactly one reversal");
  const rejectSql = (sql, expected) => {
    try {
      compose(
        "exec",
        "-T",
        "postgres",
        "psql",
        "-U",
        "payment_app",
        "-d",
        "PayDB",
        "-v",
        "ON_ERROR_STOP=1",
        "-c",
        sql,
      );
      assert.fail("Database accepted invalid ledger mutation");
    } catch (error) {
      assert(String(error.stderr).includes(expected), "Database guard " + expected);
    }
  };
  rejectSql(
    `BEGIN; UPDATE "Journals" SET "Kind" = 'Edited' WHERE "PaymentId" = '${trip.id}'; ROLLBACK;`,
    "Ledger is append-only",
  );
  const invalidLines = JSON.stringify([
    { account: "A", side: "Debit", amount: 100 },
    { account: "B", side: "Credit", amount: 99 },
  ]);
  rejectSql(
    `BEGIN; INSERT INTO "Journals" ("Id","PaymentId","Kind","Lines","CreatedAt") VALUES ('${randomUUID()}','${trip.id}','Invalid','${invalidLines}',now()); ROLLBACK;`,
    "CK_Journal_Balanced",
  );
  console.log(
    "PASS duplicate bus messages, Ops cash refund, audited reversal and database ledger guards",
  );

  await click(user, "Đặt chuyến mới");
  await click(user, "Tìm xe và xem giá");
  await click(user, "Từ chối giá");
  await wait(
    user,
    `document.querySelector('[aria-label="Chi tiết chuyến"]')?.innerText.includes('Đã hủy')`,
    "User cancels quote",
  );
  await call("Network.setBlockedURLs", { urls: ["*/api/backend/profiles/me"] }, user);
  await evaluate(user, `window.location.hash = 'profile'`);
  await wait(
    user,
    `!!document.querySelector('#profile [role="alert"]')`,
    "profile load failure shown",
  );
  assert(
    await evaluate(
      user,
      `Array.from(document.querySelectorAll('#profile input')).every(input => input.disabled)`,
    ),
    "Failed profile load must not allow overwriting saved data",
  );
  await call("Network.setBlockedURLs", { urls: [] }, user);
  await click(user, "Thử lại");
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
  await evaluate(user, `window.location.hash = 'ride/${trip.id}'`);
  await call("Page.reload", {}, user);
  await wait(
    user,
    `document.querySelector('.cash-receipt')?.innerText.includes('Đã hoàn tiền mặt')`,
    "direct trip URL survives reload",
  );
  assert.equal((await request(user, "trips/" + trip.id)).body.status, "Paid");
  console.log("PASS direct trip URL and refunded receipt after reload");
  const legacyTrip = (await request(ops, "trips")).body.find(
    (item) =>
      item.method !== "Cod" && ["Paid", "PaymentPending", "PaymentReview"].includes(item.status),
  );
  if (legacyTrip) {
    await evaluate(ops, `window.location.hash = 'ride/${legacyTrip.id}'`);
    await wait(
      ops,
      `!!document.querySelector('[aria-label="Giao dịch cũ"]')`,
      "legacy payment remains read-only",
    );
    assert(
      !(await evaluate(ops, `!!document.querySelector('[aria-label="Thanh toán tiền mặt"]')`)),
    );
    assert.equal(
      (
        await request(ops, `trips/${legacyTrip.id}/retry-payment`, {
          expectedVersion: legacyTrip.version,
        })
      ).status,
      409,
    );
    await evaluate(ops, `window.location.hash = 'payments'`);
    console.log("PASS legacy online history never becomes COD or accepts payment retry");
  }

  for (const [session, name] of [
    [user, "user-mobile"],
    [driver, "driver-mobile"],
    [ops, "ops-mobile"],
  ]) {
    await call("Page.bringToFront", {}, session);
    await call(
      "Emulation.setDeviceMetricsOverride",
      { width: 360, height: 800, deviceScaleFactor: 1, mobile: true },
      session,
    );
    if (name !== "ops-mobile") await evaluate(session, `window.location.hash = 'ride'`);
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
  await evaluate(driver, `window.location.hash = 'ride'`);
  if (
    await evaluate(
      driver,
      `Array.from(document.querySelectorAll('button')).some(b => b.textContent.trim() === 'Đóng chi tiết chuyến')`,
    )
  )
    await click(driver, "Đóng chi tiết chuyến");
  await click(driver, "Chuyển ngoại tuyến");
  console.log("PASS User/Driver/Ops 360px responsive layout, keyboard focus and no visible errors");
  if (process.argv.includes("--refresh")) {
    console.log("Checking server-side access-token refresh after 300-second lifespan...");
    for (let i = 0; i < 31; i++) await delay(10000);
    assert.equal((await request(user, "profiles/me")).status, 200);
    console.log("PASS browser access-token refresh after expiry");
  }
  await evaluate(user, `window.location.hash = 'profile'`);
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
  if (injectFaults && faultActive) {
    try {
      compose("start", "rabbitmq", "trip", "payment");
    } catch {
      /* verify.sh stops project on failure */
    }
  }
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
