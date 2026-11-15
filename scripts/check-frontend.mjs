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
      "--rootDir",
      "frontend/src",
      "--outDir",
      output,
      "frontend/src/features/rides/application/RideService.ts",
      "frontend/src/features/payments/infrastructure/HttpWalletGateway.ts",
    ],
    { cwd: root, stdio: "inherit" },
  );
  const require = createRequire(import.meta.url);
  const { RideService } = require(join(output, "features/rides/application/RideService.js"));
  const { availableActions, terminal, isActiveTrip, latestTrip, canReviewTrip } = require(
    join(output, "features/rides/domain/trip-rules.js"),
  );
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
  };
  const service = new RideService(gateway);
  const booking = {
    pickup: { latitude: 10, longitude: 106, label: "From" },
    destination: { latitude: 11, longitude: 106, label: "To" },
    method: "Cod",
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
  assert.throws(() => service.act(trip, "pickup", "rider"), /không hợp lệ/);
  assert.equal(await service.act(trip, "confirm-quote", "rider"), "confirm-quote");
  for (const status of ["DriverAccepted", "EnRoute"]) {
    assert(availableActions({ status }, "rider").includes("cancel"));
    assert.deepEqual(availableActions({ status }, "observer"), []);
  }
  assert(!availableActions({ status: "PickedUp" }, "rider").includes("cancel"));
  for (const status of ["Paid", "Cancelled", "Failed", "PaymentReview"]) {
    assert.deepEqual(availableActions({ status }, "rider"), []);
  }
  assert(
    !terminal.has("PaymentPending") && !terminal.has("PaymentReview"),
    "cash collection remains active until settled",
  );
  assert(terminal.has("Paid"));
  assert(isActiveTrip({ status: "PaymentPending", method: "Cod" }));
  assert(
    isActiveTrip({ status: "PaymentPending", method: "Wallet" }),
    "wallet settlement remains active until confirmed",
  );
  const oldTrip = { id: "trip", version: 10, status: "PaymentPending", rating: null };
  const reviewed = { ...oldTrip, version: 12, status: "Paid", rating: 5 };
  assert.equal(latestTrip(oldTrip, reviewed), reviewed, "details must use the latest version");
  assert.equal(latestTrip(reviewed, oldTrip), reviewed, "late polling must not undo a review");
  assert.equal(latestTrip(null, undefined), undefined);
  const walletReview = { status: "PaymentReview", method: "Wallet", history: [] };
  assert.deepEqual(availableActions(walletReview, "rider"), ["cancel"]);
  assert.equal(canReviewTrip(walletReview), false, "failed wallet hold is not a completed ride");
  const droppedOff = { ...walletReview, history: [{ status: "DroppedOff" }] };
  assert.deepEqual(availableActions(droppedOff, "rider"), []);
  assert.equal(canReviewTrip(droppedOff), true);

  const { HttpWalletGateway } = require(
    join(output, "features/payments/infrastructure/HttpWalletGateway.js"),
  );
  const { ApiRequestError } = require(
    join(output, "features/payments/application/ApiRequestError.js"),
  );
  const originalFetch = globalThis.fetch;
  const originalStorage = Object.getOwnPropertyDescriptor(globalThis, "sessionStorage");
  const storage = new Map();
  Object.defineProperty(globalThis, "sessionStorage", {
    configurable: true,
    value: {
      getItem: (key) => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, value),
      removeItem: (key) => storage.delete(key),
    },
  });
  try {
    for (const action of ["topUp", "withdraw"]) {
      const wallet = new HttpWalletGateway();
      const requests = [];
      globalThis.fetch = async (url, options) => {
        requests.push({ url, key: options.headers["Idempotency-Key"], body: options.body });
        if (requests.length === 1) {
          throw new Error("Response lost after commit");
        }
        return Response.json({ id: "transaction" });
      };
      const invoke = () =>
        action === "topUp" ? wallet.topUp(10000) : wallet.withdraw({ amount: 10000 });
      await assert.rejects(invoke(), /Response lost/);
      await invoke();
      assert.equal(requests[0].key, requests[1].key, `${action}: retry must retain key`);
      assert.equal(requests[0].body, requests[1].body);
      await invoke();
      assert.notEqual(requests[1].key, requests[2].key, `${action}: new request needs new key`);
      assert.equal(storage.size, 0);
    }
    const wallet = new HttpWalletGateway();
    globalThis.fetch = async () => new Response("Unavailable", { status: 503 });
    await assert.rejects(wallet.balance(), (error) => {
      return error instanceof ApiRequestError && error.status === 503;
    });
    globalThis.fetch = async () => new Response("Invalid JSON", { status: 200 });
    await assert.rejects(wallet.topUp(10000), SyntaxError);
    assert.equal(storage.size, 1, "an unreadable response must retain the financial request key");
  } finally {
    globalThis.fetch = originalFetch;
    if (originalStorage) {
      Object.defineProperty(globalThis, "sessionStorage", originalStorage);
    } else {
      delete globalThis.sessionStorage;
    }
  }
  console.log(
    "Frontend checks passed: booking/wallet retries, latest trip version, Rider actions/reviews.",
  );
} finally {
  rmSync(output, { recursive: true, force: true });
}
