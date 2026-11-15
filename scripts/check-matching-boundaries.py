"""Check two trips contesting one Driver and no nearby Driver, without resetting data."""

from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import time
import urllib.request
import uuid


def main():
    settings = dict(
        line.split("=", 1)
        for line in (Path(__file__).resolve().parents[1] / ".env")
        .read_text(encoding="utf-8")
        .splitlines()
        if "=" in line and not line.startswith("#")
    )

    def call(path, token=None, body=None):
        headers = {"Content-Type": "application/json", "Idempotency-Key": str(uuid.uuid4())}
        if token:
            headers["Authorization"] = "Bearer " + token
        request = urllib.request.Request(
            "http://localhost:8080" + path,
            None if body is None else json.dumps(body).encode(),
            headers,
        )
        with urllib.request.urlopen(request, timeout=20) as response:
            raw = response.read()
            return json.loads(raw) if raw else None

    def login(username, key):
        return call("/auth/login", body={"username": username, "password": settings[key]})[
            "accessToken"
        ]

    def wait(read, predicate, label):
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            value = read()
            if predicate(value):
                return value
            time.sleep(1)
        raise RuntimeError("Timed out: " + label)

    riders = [login(name, "RIDER_PASSWORD") for name in ("rider083", "rider084")]
    driver = login("driver0001", "DRIVER_PASSWORD")
    profile = call("/drivers/me", driver)
    assert profile["available"] and not profile["busy"], "Use an idle Driver0001 fixture"
    pickup = {
        "latitude": profile["latitude"],
        "longitude": profile["longitude"],
        "label": "Idle Driver fixture",
    }
    destination = {"latitude": 10.7599, "longitude": 106.6822, "label": "Test destination"}
    created = []
    try:
        for rider in riders:
            trip = call(
                "/trips", rider, {"pickup": pickup, "destination": destination, "method": "Cod"}
            )
            created.append((rider, trip["id"]))
        quotes = [
            wait(
                lambda r=rider, i=trip_id: call("/trips/" + i, r),
                lambda trip: trip["status"] == "AwaitingRiderConfirmation",
                "quote",
            )
            for rider, trip_id in created
        ]
        assert quotes[0]["driverId"] == quotes[1]["driverId"], "Fixture did not select same Driver"

        def confirm(index):
            rider, trip_id = created[index]
            quote = quotes[index]
            call(
                "/trips/" + trip_id + "/confirm-quote",
                rider,
                {"expectedVersion": quote["version"], "quoteId": quote["quote"]["id"]},
            )

        with ThreadPoolExecutor(max_workers=2) as pool:
            list(pool.map(confirm, range(2)))
        states = wait(
            lambda: [call("/trips/" + i, r) for r, i in created],
            lambda trips: any(t["status"] == "AwaitingDriverAcceptance" for t in trips)
            and any(t["status"] == "AwaitingRiderConfirmation" for t in trips),
            "one invitation and one rematch",
        )
        winner = next(t for t in states if t["status"] == "AwaitingDriverAcceptance")
        loser = next(t for t in states if t["status"] == "AwaitingRiderConfirmation")
        assert winner["driverId"] == quotes[0]["driverId"]
        assert loser["driverId"] != winner["driverId"], "Losing trip reused reserved Driver"
        offers = call("/drivers/me/offers", driver)
        assert sum(t["id"] in {i for _, i in created} for t in offers) == 1
        print(
            "PASS: concurrent confirmations produce one reservation; loser gets a new quote",
            flush=True,
        )
    finally:
        for rider, trip_id in created:
            trip = call("/trips/" + trip_id, rider)
            if trip["status"] not in ("Failed", "Cancelled", "Paid"):
                call(
                    "/trips/" + trip_id + "/cancel",
                    rider,
                    {"expectedVersion": trip["version"], "reason": "Concurrency test cleanup"},
                )

    # Deliberately outside the central seeded Driver area; matching fails before requesting a route.
    trip = call(
        "/trips",
        riders[0],
        {
            "pickup": {"latitude": 10.4, "longitude": 106.9, "label": "No-driver fixture"},
            "destination": destination,
            "method": "Wallet",
        },
    )
    failed = wait(
        lambda: call("/trips/" + trip["id"], riders[0]),
        lambda value: value["status"] == "Failed",
        "no nearby Driver",
    )
    assert not failed["driverId"] and not failed["quote"]
    print("PASS: no nearby Driver ends Failed without quote or invitation", flush=True)


if __name__ == "__main__":
    main()
