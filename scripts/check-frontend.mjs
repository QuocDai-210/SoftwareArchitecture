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
      "frontend/src/features/rides/application/RideService.ts",
      "frontend/src/features/rides/presentation/trip-timeline.ts",
    ],
    { cwd: root, stdio: "inherit" },
  );
  const require = createRequire(import.meta.url);
  const { RideService } = require(join(output, "application/RideService.js"));
  const { availableActions, terminal, isActiveTrip } = require(
    join(output, "domain/trip-rules.js"),
  );
  const { matchingProgress, tripTimeline } = require(join(output, "presentation/trip-timeline.js"));
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
    method: "Cod",
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
  for (const status of ["DriverAccepted", "EnRoute"]) {
    assert(availableActions({ status }, "rider").includes("cancel"));
    assert(availableActions({ status }, "driver").includes("cancel"));
    assert.deepEqual(availableActions({ status }, "observer"), []);
  }
  assert(!availableActions({ status: "PickedUp" }, "rider").includes("cancel"));
  assert(!availableActions({ status: "PickedUp" }, "driver").includes("cancel"));
  for (const status of ["Paid", "Cancelled", "Failed", "PaymentReview"]) {
    assert.deepEqual(availableActions({ status }, "rider"), []);
    assert.deepEqual(availableActions({ status }, "driver"), []);
  }
  assert(
    !terminal.has("PaymentPending") && !terminal.has("PaymentReview"),
    "cash collection remains active until settled",
  );
  assert(terminal.has("Paid"));
  assert(isActiveTrip({ status: "PaymentPending", method: "Cod" }));
  assert(
    !isActiveTrip({ status: "PaymentPending", method: "Wallet" }),
    "frozen legacy payment never blocks new COD booking",
  );
  const cashAttempts = [];
  gateway.confirmCash = async (id, reason, key) => {
    cashAttempts.push({ id, reason, key });
    if (cashAttempts.length === 1) throw new Error("Lost cash response");
    return { status: "Settled" };
  };
  await assert.rejects(service.confirmCash("trip"), /Lost cash response/);
  await service.confirmCash("trip");
  assert.equal(cashAttempts[0].key, cashAttempts[1].key, "cash retry retains idempotency key");
  await service.confirmCash("trip", "Ops correction");
  assert.notEqual(
    cashAttempts[1].key,
    cashAttempts[2].key,
    "different cash payload needs separate key",
  );
  await service.toggleAvailability({ available: false }, 10, 106);
  assert.deepEqual(attempts.slice(-2), [
    ["location", 10, 106],
    ["availability", true],
  ]);
  const requestedTimeline = tripTimeline("Requested", "2026-11-07T10:00:00Z", [
    { status: "Requested", at: "2026-11-07T10:00:00Z", reason: null },
  ]);
  assert.equal(requestedTimeline[0].state, "current");
  assert.equal(requestedTimeline[1].state, "upcoming");
  assert.match(matchingProgress("Requested", 1), /Có 1\/3 lượt mời/);
  assert.match(matchingProgress("Requested", 3), /Có 3\/3 lượt mời/);
  assert.equal(matchingProgress("Requested", 0), null);
  assert.equal(matchingProgress("AwaitingDriverAcceptance", 1), null);
  const retriedMatchTimeline = tripTimeline("Requested", "2026-11-07T10:00:00Z", [
    { status: "Requested", at: "2026-11-07T10:00:00Z", reason: null },
    { status: "AwaitingDriverAcceptance", at: "2026-11-07T10:02:00Z", reason: null },
    { status: "Requested", at: "2026-11-07T10:03:00Z", reason: "Tài xế từ chối" },
  ]);
  assert.equal(retriedMatchTimeline[0].state, "current");
  assert.equal(retriedMatchTimeline[1].state, "upcoming");
  const enRouteTimeline = tripTimeline("EnRoute", "2026-11-07T10:00:00Z", [
    { status: "Requested", at: "2026-11-07T10:00:00Z", reason: null },
    { status: "DriverAccepted", at: "2026-11-07T10:05:00Z", reason: null },
    { status: "EnRoute", at: "2026-11-07T10:06:00Z", reason: null },
  ]);
  assert.equal(enRouteTimeline[0].state, "complete");
  assert.equal(enRouteTimeline[1].state, "complete");
  assert.equal(enRouteTimeline[2].state, "current");
  const paymentTimeline = tripTimeline("PaymentPending", "2026-11-07T10:00:00Z", [
    { status: "Requested", at: "2026-11-07T10:00:00Z", reason: null },
    { status: "DriverAccepted", at: "2026-11-07T10:05:00Z", reason: null },
    { status: "EnRoute", at: "2026-11-07T10:06:00Z", reason: null },
    { status: "PickedUp", at: "2026-11-07T10:10:00Z", reason: null },
    { status: "DroppedOff", at: "2026-11-07T10:20:00Z", reason: null },
    { status: "PaymentPending", at: "2026-11-07T10:20:00Z", reason: null },
  ]);
  assert.equal(paymentTimeline[4].state, "complete");
  assert.equal(paymentTimeline[5].state, "current");
  const cancelledTimeline = tripTimeline("Cancelled", "2026-11-07T10:00:00Z", [
    { status: "Requested", at: "2026-11-07T10:00:00Z", reason: null },
    { status: "DriverAccepted", at: "2026-11-07T10:05:00Z", reason: null },
    { status: "EnRoute", at: "2026-11-07T10:06:00Z", reason: null },
    { status: "Cancelled", at: "2026-11-07T10:08:00Z", reason: "Đổi kế hoạch" },
  ]);
  assert.equal(cancelledTimeline.length, 4);
  assert.equal(cancelledTimeline[2].state, "complete");
  assert.equal(cancelledTimeline[3].title, "Chuyến đã hủy");
  assert.equal(cancelledTimeline[3].description, "Đổi kế hoạch");
  const failedTimeline = tripTimeline("Failed", "2026-11-07T10:00:00Z", [
    { status: "Requested", at: "2026-11-07T10:00:00Z", reason: null },
    { status: "Failed", at: "2026-11-07T10:04:00Z", reason: "Không còn tài xế phù hợp" },
  ]);
  assert.equal(failedTimeline.length, 2);
  assert.equal(failedTimeline[1].title, "Không tìm được xe");
  assert.equal(failedTimeline[1].description, "Không còn tài xế phù hợp");
  const paidTimeline = tripTimeline("Paid", "2026-11-07T10:00:00Z", [
    { status: "Requested", at: "2026-11-07T10:00:00Z", reason: null },
    { status: "DriverAccepted", at: "2026-11-07T10:05:00Z", reason: null },
    { status: "EnRoute", at: "2026-11-07T10:06:00Z", reason: null },
    { status: "PickedUp", at: "2026-11-07T10:10:00Z", reason: null },
    { status: "DroppedOff", at: "2026-11-07T10:20:00Z", reason: null },
    { status: "PaymentPending", at: "2026-11-07T10:20:00Z", reason: null },
    { status: "Paid", at: "2026-11-07T10:25:00Z", reason: null },
  ]);
  assert.equal(paidTimeline.at(-1).state, "complete");
  assert.equal(paidTimeline.at(-1).description, "Đã xác nhận thanh toán");
  console.log(
    "Frontend checks passed: booking idempotency, state/role actions, driver presence, trip timeline.",
  );
} finally {
  rmSync(output, { recursive: true, force: true });
}
