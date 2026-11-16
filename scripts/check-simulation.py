"""Exercise a real seeded ride through Gateway. Creates retained simulation history."""

import argparse
import json
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rider", default="rider098")
    parser.add_argument("--method", choices=["Wallet", "Cod"], default="Wallet")
    parser.add_argument("--rejections", type=int, choices=[1, 4], default=1)
    parser.add_argument(
        "--scenario",
        choices=["ride", "insufficient", "payment-review", "rematch", "broker-recovery", "restart"],
        default="ride",
    )
    parser.add_argument("--base-url", default="http://localhost:8080")
    parser.add_argument("--expected-amount", type=int)
    args = parser.parse_args()
    if args.scenario == "payment-review" and args.method != "Cod":
        parser.error("payment-review requires --method Cod and a configured local failure fixture")
    if args.scenario == "broker-recovery" and args.method != "Wallet":
        parser.error("broker-recovery requires Wallet")

    def compose(*arguments):
        subprocess.run(
            ["docker", "compose", *arguments],
            cwd=Path(__file__).resolve().parents[1],
            check=True,
            timeout=90,
        )

    settings = {}
    for line in (
        (Path(__file__).resolve().parents[1] / ".env").read_text(encoding="utf-8").splitlines()
    ):
        if "=" in line and not line.startswith("#"):
            name, value = line.split("=", 1)
            settings[name] = value.strip().strip('"').strip("'")

    def request(path, token=None, body=None, method=None, key=None, expected=None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        if key:
            headers["Idempotency-Key"] = key
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(args.base_url + path, data, headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=20) as response:
                raw = response.read()
                if expected is not None:
                    assert response.status == expected, f"{path}: expected HTTP {expected}"
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            if error.code == expected:
                return None
            correlation = error.headers.get("CorrelationId", "unavailable")
            raise RuntimeError(
                f"{req.method} {path}: HTTP {error.code}; correlation={correlation}"
            ) from None

    def wait(read, predicate, label):
        deadline = time.monotonic() + 60
        last = None
        while time.monotonic() < deadline:
            last = read()
            if predicate(last):
                return last
            time.sleep(1)
        status = last.get("status", last.get("stage")) if isinstance(last, dict) else None
        raise RuntimeError(f"Timed out: {label}; last status={status}")

    def login(username, password_key):
        return request(
            "/auth/login", body={"username": username, "password": settings[password_key]}
        )["accessToken"]

    rider = login(args.rider, "RIDER_PASSWORD")
    before = request("/wallets/me", rider)
    if args.scenario == "insufficient":
        if args.method != "Wallet" or before["held"] != 0:
            raise RuntimeError("Insufficient-funds fixture requires Wallet and no existing holds")
        if before["available"]:
            if not 10000 <= before["available"] <= 5000000:
                raise RuntimeError("Use a fixture with zero or 10,000-5,000,000 available")
            key = str(uuid.uuid4())
            withdrawal = request(
                "/wallets/withdrawals", rider, {"amount": before["available"]}, key=key
            )
            replay = request(
                "/wallets/withdrawals", rider, {"amount": before["available"]}, key=key
            )
            assert withdrawal["id"] == replay["id"], "Withdrawal replay created another request"
            wait(
                lambda: request("/wallets/me", rider),
                lambda wallet: wallet["available"] == 0 and wallet["held"] == 0,
                "Simulated withdrawal completion",
            )
        before = request("/wallets/me", rider)
    trip = request(
        "/trips",
        rider,
        {
            "pickup": {"latitude": 10.7769, "longitude": 106.7009, "label": "Test pickup"},
            "destination": {
                "latitude": 10.7599,
                "longitude": 106.6822,
                "label": "Test destination",
            },
            "method": args.method,
        },
        key=str(uuid.uuid4()),
    )
    path = "/trips/" + trip["id"]
    print(f"Created {args.method} trip {trip['id']}", flush=True)

    def read_trip():
        return request(path, rider)

    def state(name):
        return wait(read_trip, lambda item: item["status"] == name, name)

    trip = state("AwaitingRiderConfirmation")
    request(
        path + "/confirm-quote",
        rider,
        {"expectedVersion": trip["version"] - 1, "quoteId": trip["quote"]["id"]},
        expected=409,
    )
    amount = trip["quote"]["amount"]
    if args.expected_amount is not None:
        assert amount == args.expected_amount, f"Expected fare {args.expected_amount}, got {amount}"
    driver_id = trip["driverId"]
    driver_username = f"driver{int(driver_id.split('-')[-1]):04d}"
    driver = login(driver_username, "DRIVER_PASSWORD")
    driver_before = request("/wallets/me", driver)
    outsider = login("rider001" if args.rider != "rider001" else "rider002", "RIDER_PASSWORD")
    request(path, outsider, expected=403)
    request(path + "/driver-accept", rider, {"expectedVersion": trip["version"]}, expected=403)
    try:
        if args.scenario == "broker-recovery":
            compose("stop", "rabbitmq")
        request(
            path + "/confirm-quote",
            rider,
            {"expectedVersion": trip["version"], "quoteId": trip["quote"]["id"]},
        )
        if args.scenario == "broker-recovery":
            assert read_trip()["status"] == "WalletHoldPending", "Workflow advanced without broker"
            assert request("/wallets/me", rider)["held"] == before["held"], "Unexpected hold"
    finally:
        if args.scenario == "broker-recovery":
            compose("up", "-d", "--no-deps", "--wait", "rabbitmq")
    if args.scenario == "insufficient":
        trip = wait(
            read_trip,
            lambda item: item["status"] == "AwaitingRiderConfirmation"
            and any(change["status"] == "PaymentReview" for change in item["history"]),
            "Insufficient-funds compensation",
        )
        assert not any(
            change["status"] == "AwaitingDriverAcceptance" for change in trip["history"]
        ), "Driver invited without funds"
        offers = request("/drivers/me/offers", driver)
        assert not any(offer["id"] == trip["id"] for offer in offers), "Unexpected driver offer"
        wallet = request("/wallets/me", rider)
        assert wallet["available"] == 0 and wallet["held"] == 0, "Insufficient-funds hold leaked"
        request(
            path + "/cancel",
            rider,
            {"expectedVersion": trip["version"], "reason": "Insufficient-funds test cleanup"},
        )
        state("Cancelled")
        print(
            "PASS: insufficient funds, no driver invitation, no hold; fixture balance remains zero",
            flush=True,
        )
        return
    trip = state("AwaitingDriverAcceptance")
    if args.method == "Wallet":
        held = request("/wallets/me", rider)
        assert held["available"] == before["available"] - amount, "Incorrect available balance"
        assert held["held"] == before["held"] + amount, "Missing wallet hold"
    if args.scenario == "broker-recovery":
        request(
            path + "/cancel",
            rider,
            {"expectedVersion": trip["version"], "reason": "Broker recovery test cleanup"},
        )
        state("Cancelled")
        wait(
            lambda: request("/wallets/me", rider),
            lambda wallet: wallet["available"] == before["available"]
            and wallet["held"] == before["held"],
            "Wallet compensation after broker recovery",
        )
        print(
            "PASS: outbox recovered after broker restart; cancellation restored the wallet",
            flush=True,
        )
        return
    rejections = 0
    while args.scenario == "rematch" and rejections < args.rejections:
        previous_quote = trip["quote"]["id"]
        previous_driver = trip["driverId"]
        request(path + "/driver-reject", driver, {"expectedVersion": trip["version"]})
        rejections += 1
        if rejections == 4:
            state("Failed")
            restored = request("/wallets/me", rider)
            assert restored["available"] == before["available"], "Money lost after fourth rejection"
            assert restored["held"] == before["held"], "Hold leaked after fourth rejection"
            print("PASS: fourth rejection ends Failed with all wallet holds released", flush=True)
            return
        trip = state("AwaitingRiderConfirmation")
        restored = request("/wallets/me", rider)
        assert restored["available"] == before["available"], "Previous hold was not returned"
        assert restored["held"] == before["held"], "Previous quote still holds funds"
        assert trip["quote"]["id"] != previous_quote, "Rematch reused old quote"
        assert trip["driverId"] != previous_driver, "Rejected driver was not excluded"
        amount = trip["quote"]["amount"]
        driver_id = trip["driverId"]
        driver_username = f"driver{int(driver_id.split('-')[-1]):04d}"
        driver = login(driver_username, "DRIVER_PASSWORD")
        driver_before = request("/wallets/me", driver)
        request(
            path + "/confirm-quote",
            rider,
            {"expectedVersion": trip["version"], "quoteId": trip["quote"]["id"]},
        )
        trip = state("AwaitingDriverAcceptance")
        held = request("/wallets/me", rider)
        if args.method == "Wallet":
            assert (
                held["available"] == before["available"] - amount
            ), "New quote did not debit funds"
            assert held["held"] == before["held"] + amount, "New quote has no real hold"
        print("PASS: rejected Driver excluded; old hold returned and new quote held", flush=True)
    offers = request("/drivers/me/offers", driver)
    assert any(offer["id"] == trip["id"] for offer in offers), "Assigned driver cannot see offer"
    request(path + "/driver-accept", driver, {"expectedVersion": trip["version"]})
    state("DriverAccepted")
    print(f"Driver {driver_username} accepted; amount={amount}", flush=True)
    simulation_path = "/simulations/" + driver_id
    for action, stage in [("en-route", "EnRoute"), ("pickup", "PickedUp")]:
        trip = read_trip()
        request(path + "/" + action, driver, {"expectedVersion": trip["version"]})
        wait(
            lambda: request(simulation_path, driver),
            lambda item: item is not None and item["stage"] == stage,
            stage,
        )
        if stage == "PickedUp":
            request(simulation_path, driver, {"speed": 1, "paused": True}, method="PUT")
            current_trip = read_trip()
            request(
                path + "/dropoff",
                driver,
                {"expectedVersion": current_trip["version"]},
                expected=409,
            )
            assert read_trip()["status"] == "PickedUp", "Early dropoff changed trip state"
            print("PASS: dropoff rejected before simulated arrival", flush=True)
        if args.scenario == "restart" and stage == "EnRoute":
            paused = request(simulation_path, driver, {"speed": 20, "paused": True}, method="PUT")
            compose("restart", "matching")
            compose("up", "-d", "--no-deps", "--wait", "matching")
            resumed = request(simulation_path, driver)
            assert resumed["tripId"] == trip["id"] and resumed["paused"], "Restart lost simulation"
            assert resumed["progress"] == paused["progress"], "Paused progress changed on restart"
            print("PASS: Matching restart retained the paused route and progress", flush=True)
        request(simulation_path, driver, {"speed": 20, "paused": False}, method="PUT")
        wait(
            lambda: request(simulation_path, driver),
            lambda item: item["arrived"],
            stage + " arrival",
        )
        print(f"Arrived: {stage}", flush=True)
    trip = read_trip()
    request(path + "/dropoff", driver, {"expectedVersion": trip["version"]})
    payment_path = "/payments/" + trip["id"]
    if args.scenario == "payment-review":
        state("PaymentReview")
        print(
            f"PASS: COD failure entered PaymentReview; trip={trip['id']}, driver={driver_username}",
            flush=True,
        )
        return
    if args.method == "Cod":
        state("PaymentPending")
        # Payment creation is asynchronous; history avoids treating an expected 404 as success.
        wait(
            lambda: request("/payments/history", driver),
            lambda page: any(item["id"] == trip["id"] for item in page["items"]),
            "COD creation",
        )
        key = str(uuid.uuid4())
        for _ in range(2):
            request(payment_path + "/confirm-cash", driver, {"received": True}, key=key)
    state("Paid")
    wait(
        lambda: request("/drivers/me", driver),
        lambda profile: not profile["busy"],
        "Driver release after payment",
    )
    payment = request(payment_path, driver)
    assert payment["status"] == "Settled", "Assigned Driver cannot read settled payment"
    journals = request(payment_path + "/ledger", driver)
    assert journals, "No financial journal for completed trip"
    for journal in journals:
        debits = sum(line["amount"] for line in journal["lines"] if line["side"] == "Debit")
        credits = sum(line["amount"] for line in journal["lines"] if line["side"] == "Credit")
        assert debits == credits, "Unbalanced trip journal"
    after = request("/wallets/me", rider)
    driver_after = request("/wallets/me", driver)
    expected = amount if args.method == "Wallet" else 0
    assert after["available"] == before["available"] - expected, "Rider settlement mismatch"
    assert after["held"] == before["held"], "Hold remains after settlement"
    assert (
        driver_after["available"] == driver_before["available"] + expected
    ), "Driver credited incorrectly"
    trip = read_trip()
    review = {"expectedVersion": trip["version"], "rating": 5, "comment": "Simulation test"}
    request(path + "/review", driver, review, expected=403)
    request(path + "/review", rider, {**review, "rating": 6}, expected=400)
    first = request(path + "/review", rider, review)
    replay = request(path + "/review", rider, review)
    assert first["version"] == replay["version"], "Repeated review changed version"
    request(path + "/review", rider, {**review, "rating": 4}, expected=409)
    print(f"PASS: {args.method} Paid, rider and driver balances verified", flush=True)


if __name__ == "__main__":
    main()
