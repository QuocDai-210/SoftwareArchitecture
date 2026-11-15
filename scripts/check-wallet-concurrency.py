"""Check local wallet concurrency and API permissions; retains simulated transactions."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import time
import urllib.error
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rider", default="rider094")
    parser.add_argument("--base-url", default="http://localhost:8080")
    args = parser.parse_args()
    settings = {}
    for line in (
        (Path(__file__).resolve().parents[1] / ".env").read_text(encoding="utf-8").splitlines()
    ):
        if "=" in line and not line.startswith("#"):
            name, value = line.split("=", 1)
            settings[name] = value.strip().strip('"').strip("'")

    def call(path, token=None, body=None, key=None, expected=None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        if key:
            headers["Idempotency-Key"] = key
        req = urllib.request.Request(
            args.base_url + path, None if body is None else json.dumps(body).encode(), headers
        )
        try:
            with urllib.request.urlopen(req, timeout=20) as response:
                status, raw = response.status, response.read()
        except urllib.error.HTTPError as error:
            status, raw = error.code, error.read()
        if expected is None:
            assert 200 <= status < 300, f"{req.method} {path}: HTTP {status}"
        else:
            assert status == expected, f"{path}: expected {expected}, got {status}"
        return json.loads(raw) if raw else None

    def login(username, password):
        return call("/auth/login", body={"username": username, "password": password})["accessToken"]

    rider = login(args.rider, settings["RIDER_PASSWORD"])
    driver = login("driver0001", settings["DRIVER_PASSWORD"])
    call(
        "/auth/login",
        body={"username": args.rider, "password": "incorrect-test-password"},
        expected=401,
    )
    call("/drivers/me/offers", rider, expected=403)
    call("/wallets/topups", driver, {"amount": 10000}, str(uuid.uuid4()), expected=403)
    call("/payments", rider, {}, str(uuid.uuid4()), expected=403)
    for token in (rider, driver):
        for path in ("/match", "/reservations", "/reservations/confirm", "/arrivals"):
            call(path, token, {}, str(uuid.uuid4()), expected=403)
    call("/wallets/me", "invalid-token", expected=401)
    before = call("/wallets/me", rider)
    assert before["held"] == 0, "Use an idle fixture with no held money"
    for amount in (0, 9999, 5000001, 10000.5):
        call("/wallets/topups", rider, {"amount": amount}, str(uuid.uuid4()), expected=400)
    key = str(uuid.uuid4())
    with ThreadPoolExecutor(max_workers=4) as pool:
        results = list(
            pool.map(lambda _: call("/wallets/topups", rider, {"amount": 10000}, key), range(4))
        )
    # PostgreSQL stores microsecond precision; compare identity and financial effects, not timestamp text.
    for field in ("reference", "journalId", "amount", "availableAfter", "heldAfter"):
        assert all(
            result[field] == results[0][field] for result in results
        ), f"Top-up replay differs: {field}"
    balance = call("/wallets/me", rider)
    assert balance["available"] == before["available"] + 10000, "Top-up credited more than once"
    call("/wallets/topups", rider, {"amount": 20000}, key, expected=409)
    print("PASS: permissions and four concurrent top-up replays", flush=True)

    with ThreadPoolExecutor(max_workers=4) as pool:
        list(
            pool.map(
                lambda _: call("/wallets/topups", rider, {"amount": 10000}, str(uuid.uuid4())),
                range(4),
            )
        )
    assert (
        call("/wallets/me", rider)["available"] == before["available"] + 50000
    ), "Lost concurrent credits"

    key = str(uuid.uuid4())
    with ThreadPoolExecutor(max_workers=4) as pool:
        results = list(
            pool.map(
                lambda _: call("/wallets/withdrawals", rider, {"amount": 50000}, key), range(4)
            )
        )
    assert len({result["id"] for result in results}) == 1, "Withdrawal duplicated"
    call("/wallets/withdrawals", rider, {"amount": 20000}, key, expected=409)
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        history = call("/wallets/withdrawals", rider)
        withdrawal = next(item for item in history["items"] if item["id"] == results[0]["id"])
        if withdrawal["state"] == "Completed":
            break
        time.sleep(1)
    else:
        raise RuntimeError("Simulated withdrawal did not complete within 60 seconds")
    after = call("/wallets/me", rider)
    assert after["available"] == before["available"], "Withdrawal debit mismatch"
    assert after["held"] == 0, "Withdrawal hold leaked"
    print("PASS: four concurrent withdrawals use one request; final balance restored", flush=True)


if __name__ == "__main__":
    main()
