// Native assert runner; compile only framework-free feature code into temporary directory.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { resolve, join } from "node:path";
import { createRequire } from "node:module";
const root = resolve(import.meta.dirname, "..");
const output = mkdtempSync(join(tmpdir(), "swiftride-check-"));
try {
  execFileSync(
    join(root, "frontend/node_modules/.bin/tsc"),
    [
      "--module",
      "commonjs",
      "--target",
      "es2022",
      "--moduleResolution",
      "node",
      "--skipLibCheck",
      "--outDir",
      output,
      "frontend/src/features/rides/application/RideService.ts",
    ],
    { cwd: root, stdio: "inherit" },
  );
  const require = createRequire(import.meta.url);
  const { RideService } = require(join(output, "application/RideService.js"));
  const { availableActions } = require(join(output, "domain/trip-rules.js"));
  const attempts = [];
  const gateway = {
    async book(booking, key) {
      attempts.push({ booking, key });
      if (attempts.length === 1) throw new Error("Connection lost");
      return { id: "trip" };
    },
    async act(trip, action) {
      return action;
    },
    async location(latitude, longitude) {
      attempts.push(["location", latitude, longitude]);
    },
    async availability(available) {
      attempts.push(["availability", available]);
    },
  };
  const service = new RideService(gateway);
  const booking = {
    pickup: { latitude: 10, longitude: 106, label: "From" },
    destination: { latitude: 11, longitude: 106, label: "To" },
    method: "Wallet",
    paymentToken: null,
  };
  await assert.rejects(service.book({ ...booking, destination: booking.pickup }), /khác nhau/);
  assert.equal(attempts.length, 0);
  await assert.rejects(service.book(booking), /Connection lost/);
  assert.equal((await service.book(booking)).id, "trip");
  assert.equal(attempts[0].key, attempts[1].key, "retry must retain booking key");
  await service.book(booking);
  assert.notEqual(attempts[1].key, attempts[2].key, "new booking needs new key");
  const trip = { id: "trip", status: "AwaitingRiderConfirmation", version: 1 };
  assert.deepEqual(availableActions(trip, "rider"), ["confirm-quote", "cancel"]);
  assert.deepEqual(availableActions(trip, "driver"), []);
  assert.throws(() => service.act(trip, "pickup", "rider"), /không hợp lệ/);
  assert.equal(await service.act(trip, "confirm-quote", "rider"), "confirm-quote");
  for (const status of ["Paid", "Cancelled", "Failed", "PaymentReview"]) {
    assert.deepEqual(availableActions({ status }, "rider"), []);
    assert.deepEqual(availableActions({ status }, "driver"), []);
  }
  await service.toggleAvailability({ available: false }, 10, 106);
  assert.deepEqual(attempts.slice(-2), [
    ["location", 10, 106],
    ["availability", true],
  ]);
  console.log("Frontend checks passed: booking idempotency, state/role actions, driver presence.");
} finally {
  rmSync(output, { recursive: true, force: true });
}
