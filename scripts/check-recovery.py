"""Run expiry and local payment fault acceptance; restores Compose fixture configuration."""

import json
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
SETTINGS = dict(
    line.split("=", 1)
    for line in (ROOT / ".env").read_text(encoding="utf-8").splitlines()
    if "=" in line and not line.startswith("#")
)


def call(path, token=None, body=None, expected=None, key=None):
    headers = {"Content-Type": "application/json", "Idempotency-Key": key or str(uuid.uuid4())}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(
        "http://127.0.0.1:8080" + path, None if body is None else json.dumps(body).encode(), headers
    )
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            raw = response.read()
            assert expected is None or response.status == expected
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        if error.code != expected:
            raise RuntimeError(
                f"{req.method} {path}: HTTP {error.code}; "
                f"correlation={error.headers.get('CorrelationId', 'unavailable')}"
            ) from None


def login(name, key):
    return call("/auth/login", body={"username": name, "password": SETTINGS[key]})["accessToken"]


def wait(read, predicate, label, seconds=60):
    deadline = time.monotonic() + seconds
    value = None
    while time.monotonic() < deadline:
        value = read()
        if predicate(value):
            return value
        time.sleep(1)
    status = value.get("status") if isinstance(value, dict) else None
    raise TimeoutError(f"{label}; last status={status}")


def compose(*args, fixture=None):
    env = os.environ.copy()
    if fixture:
        env.update(fixture)
    subprocess.run(["docker", "compose", *args], cwd=ROOT, env=env, check=True, timeout=180)


def main():
    rider = login("rider080", "RIDER_PASSWORD")
    before = call("/wallets/me", rider)
    trip = call(
        "/trips",
        rider,
        {
            "pickup": {"latitude": 10.7769, "longitude": 106.7009, "label": "Expiry test"},
            "destination": {"latitude": 10.7599, "longitude": 106.6822, "label": "Destination"},
            "method": "Wallet",
        },
    )
    path = "/trips/" + trip["id"]
    read = lambda: call(path, rider)
    quote = wait(read, lambda t: t["status"] == "AwaitingRiderConfirmation", "initial quote")
    print("Waiting for real 120-second quote expiry", flush=True)
    fresh = wait(
        read,
        lambda t: t["status"] == "AwaitingRiderConfirmation"
        and t["quote"]["id"] != quote["quote"]["id"],
        "quote expiry",
        150,
    )
    call(
        path + "/confirm-quote",
        rider,
        {"expectedVersion": fresh["version"], "quoteId": quote["quote"]["id"]},
        expected=409,
    )
    call(
        path + "/confirm-quote",
        rider,
        {"expectedVersion": fresh["version"], "quoteId": fresh["quote"]["id"]},
    )
    offer = wait(read, lambda t: t["status"] == "AwaitingDriverAcceptance", "offer")
    print("PASS: expired quote rejected; waiting for real 60-second offer expiry", flush=True)
    renewed = wait(
        read,
        lambda t: t["status"] == "AwaitingRiderConfirmation"
        and t["quote"]["id"] != fresh["quote"]["id"],
        "offer expiry",
        90,
    )
    assert renewed["driverId"] != offer["driverId"]
    wallet = call("/wallets/me", rider)
    assert wallet["held"] == before["held"] and wallet["available"] == before["available"]
    call(path + "/cancel", rider, {"expectedVersion": renewed["version"], "reason": "Test cleanup"})
    print("PASS: offer expiry rematches and returns wallet hold", flush=True)

    try:
        compose(
            "up",
            "-d",
            "--no-deps",
            "--wait",
            "payment",
            fixture={"WALLET_WITHDRAWAL_OUTCOME": "Rejected"},
        )
        withdrawal = call("/wallets/withdrawals", rider, {"amount": 10000})
        wait(
            lambda: call("/wallets/withdrawals", rider),
            lambda p: any(
                w["id"] == withdrawal["id"] and w["state"] == "Rejected" for w in p["items"]
            ),
            "withdrawal rejection",
        )
        wallet = call("/wallets/me", rider)
        assert wallet["held"] == before["held"] and wallet["available"] == before["available"]
        print("PASS: rejected withdrawal restores full available balance", flush=True)
        compose(
            "up",
            "-d",
            "--no-deps",
            "--wait",
            "payment",
            fixture={"PAYMENT_SIMULATION_OUTCOME": "Timeout"},
        )
        failure_rider = login("rider079", "RIDER_PASSWORD")
        existing = {t["id"] for t in call("/trips", failure_rider)}
        subprocess.run(
            [
                sys.executable,
                "-X",
                "utf8",
                "-B",
                "scripts/check-simulation.py",
                "--rider",
                "rider079",
                "--method",
                "Cod",
                "--scenario",
                "payment-review",
            ],
            cwd=ROOT,
            check=True,
            timeout=300,
        )
    finally:
        compose("up", "-d", "--no-deps", "--wait", "payment")
    token = login("rider079", "RIDER_PASSWORD")
    trip = next(
        t
        for t in call("/trips", token)
        if t["status"] == "PaymentReview" and t["id"] not in existing
    )
    path = "/trips/" + trip["id"]
    driver = login(f"driver{int(trip['driverId'].split('-')[-1]):04d}", "DRIVER_PASSWORD")
    compose("restart", "trip", "payment")
    compose("up", "-d", "--no-deps", "--wait", "trip", "payment")
    trip = call(path, token)
    assert trip["status"] == "PaymentReview"
    call(path + "/retry-payment", driver, {"expectedVersion": trip["version"]})
    wait(
        lambda: call("/payments/history", driver),
        lambda p: any(t["id"] == trip["id"] and t["status"] == "Pending" for t in p["items"]),
        "COD retry",
    )
    call("/payments/" + trip["id"] + "/confirm-cash", driver, {"received": True}, key=trip["id"])
    wait(lambda: call(path, token), lambda t: t["status"] == "Paid", "COD settlement")
    compose("restart", "trip", "payment")
    compose("up", "-d", "--no-deps", "--wait", "trip", "payment")
    call("/payments/" + trip["id"] + "/confirm-cash", driver, {"received": True}, key=trip["id"])
    journals = call("/payments/" + trip["id"] + "/ledger", driver)
    assert len(journals) == 1, "Repeated collection after restart created another journal"
    print(
        "PASS: timeout review survives restart, retries to Paid, collection remains idempotent",
        flush=True,
    )


if __name__ == "__main__":
    main()
