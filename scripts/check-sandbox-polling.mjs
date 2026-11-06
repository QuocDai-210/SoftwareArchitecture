import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { createRequire } from "node:module";

const root = resolve(import.meta.dirname, "..");
const output = mkdtempSync(join(tmpdir(), "swiftride-sandbox-check-"));
try {
  execFileSync(
    process.execPath,
    [
      join(root, "frontend/node_modules/typescript/bin/tsc"),
      "--module",
      "commonjs",
      "--target",
      "es2022",
      "--moduleResolution",
      "node",
      "--skipLibCheck",
      "--outDir",
      output,
      "frontend/src/features/payments/application/watchSandboxPayment.ts",
    ],
    { cwd: root, stdio: "inherit" },
  );
  const require = createRequire(import.meta.url);
  const { watchSandboxPayment } = require(join(output, "watchSandboxPayment.js"));
  const { SandboxRequestError } = require(join(output, "SandboxGateway.js"));
  let timer;
  let elapsed = 0;
  let queries = 0;
  let reads = 0;
  let state = "Pending";
  let failures = 0;
  const updates = [];
  const errors = [];
  const schedule = (action, delay) => {
    timer = () => {
      elapsed += delay;
      action();
    };
    return () => {
      timer = undefined;
    };
  };
  const flush = () => new Promise((done) => setImmediate(done));
  const gateway = {
    async reconcile() {
      queries++;
      return { state };
    },
    async get() {
      reads++;
      if (failures-- > 0) throw new Error("Transient network failure");
      return { state };
    },
  };
  const stop = watchSandboxPayment(
    gateway,
    "ref",
    (p) => updates.push(p.state),
    (message) => errors.push(message),
    schedule,
  );
  await flush();
  assert.equal(queries, 1, "Reconcile immediately on arrival");
  while (elapsed <= 180000) {
    assert(timer, "Must continue polling beyond two minutes");
    const fire = timer;
    timer = undefined;
    fire();
    await flush();
  }
  assert.equal(queries, 1, "Polling must not repeatedly call provider");
  failures = 1;
  timer();
  await flush();
  assert.match(errors.at(-1), /Transient/);
  assert(timer, "Transient failures must retain polling");
  state = "Succeeded";
  const fire = timer;
  timer = undefined;
  fire();
  await flush();
  assert.equal(updates.at(-1), "Succeeded");
  assert.equal(timer, undefined, "Stop after confirmation");
  stop();

  let resolveQuery;
  const late = new Promise((resolve) => {
    resolveQuery = resolve;
  });
  const before = updates.length;
  const cancel = watchSandboxPayment(
    { ...gateway, reconcile: () => late },
    "ref",
    (p) => updates.push(p.state),
    () => {},
    schedule,
  );
  cancel();
  resolveQuery({ state: "Pending" });
  await flush();
  assert.equal(updates.length, before, "Unmounted observers ignore in-flight responses");
  assert.equal(timer, undefined);

  watchSandboxPayment(
    {
      ...gateway,
      async reconcile() {
        throw new SandboxRequestError("Unauthorized", 401);
      },
    },
    "ref",
    () => {},
    () => {},
    schedule,
  );
  await flush();
  assert.equal(timer, undefined, "Do not loop on expired sessions");
  console.log(
    `Sandbox polling passed: immediate query, ${reads} reads, delayed IPN, recovery, cleanup.`,
  );
} finally {
  // mkdtempSync produced this exact test directory under the OS temporary root.
  assert(
    resolve(output).startsWith(resolve(tmpdir()) + "/") ||
      resolve(output).startsWith(resolve(tmpdir()) + "\\"),
  );
  assert(output.includes("swiftride-sandbox-check-"));
  rmSync(output, { recursive: true, force: true });
}
