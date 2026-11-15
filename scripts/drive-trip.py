"""Drive an existing Rider trip using its assigned seeded Driver, without creating a trip."""

import argparse
import json
from pathlib import Path
import time
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trip_id", type=uuid.UUID)
    parser.add_argument("--rider", required=True)
    parser.add_argument("--speed", type=int, choices=[1, 5, 10, 20], default=20)
    args = parser.parse_args()
    settings = dict(
        line.split("=", 1)
        for line in (Path(__file__).resolve().parents[1] / ".env")
        .read_text(encoding="utf-8")
        .splitlines()
        if "=" in line and not line.startswith("#")
    )

    def call(path, token=None, body=None, method=None):
        headers = {"Content-Type": "application/json", "Idempotency-Key": str(args.trip_id)}
        if token:
            headers["Authorization"] = "Bearer " + token
        req = urllib.request.Request(
            "http://127.0.0.1:8080" + path,
            None if body is None else json.dumps(body).encode(),
            headers,
            method=method,
        )
        with urllib.request.urlopen(req, timeout=20) as response:
            raw = response.read()
            return json.loads(raw) if raw else None

    def login(name, key):
        return call("/auth/login", body={"username": name, "password": settings[key]})[
            "accessToken"
        ]

    rider = login(args.rider, "RIDER_PASSWORD")
    path = "/trips/" + str(args.trip_id)
    deadline = time.monotonic() + 1800
    driver_id = None
    driver = None
    last = None
    while time.monotonic() < deadline:
        trip = call(path, rider)
        status = trip["status"]
        if status != last:
            print(status, flush=True)
            last = status
        if status in ("Paid", "Cancelled", "Failed"):
            return
        if status == "PaymentReview":
            raise RuntimeError("PaymentReview requires recovery; inspect payment before retrying")
        if status in (
            "Requested",
            "AwaitingRiderConfirmation",
            "WalletHoldPending",
            "ReservingDriver",
        ):
            time.sleep(1)
            continue
        if trip["driverId"] != driver_id:
            driver_id = trip["driverId"]
            username = f"driver{int(driver_id.split('-')[-1]):04d}"
            driver = login(username, "DRIVER_PASSWORD")
            print("Assigned Driver: " + username, flush=True)
        action = None
        if status == "AwaitingDriverAcceptance":
            action = "driver-accept"
        elif status == "DriverAccepted":
            action = "en-route"
        elif status in ("EnRoute", "PickedUp"):
            simulation_path = "/simulations/" + driver_id
            simulation = call(simulation_path, driver)
            if (
                simulation
                and simulation["tripId"] == str(args.trip_id)
                and simulation["stage"] == status
            ):
                if simulation["speed"] != args.speed or simulation["paused"]:
                    call(simulation_path, driver, {"speed": args.speed, "paused": False}, "PUT")
                if simulation["arrived"]:
                    action = "pickup" if status == "EnRoute" else "dropoff"
        elif status == "PaymentPending" and trip["method"] == "Cod":
            history = call("/payments/history", driver)
            payment = next((p for p in history["items"] if p["id"] == str(args.trip_id)), None)
            if payment and payment["status"] == "Pending":
                call("/payments/" + str(args.trip_id) + "/confirm-cash", driver, {"received": True})
        if action:
            call(path + "/" + action, driver, {"expectedVersion": trip["version"]})
        time.sleep(1)
    raise TimeoutError("Trip did not finish within 30 minutes")


if __name__ == "__main__":
    main()
