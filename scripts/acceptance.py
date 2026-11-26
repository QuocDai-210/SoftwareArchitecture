"""Run the Postman collection and recovery cases on a dedicated local PDF stack."""

import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
import hashlib
from html.parser import HTMLParser
from http.cookiejar import CookieJar
import json
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlencode, urlsplit
from urllib.request import (
    build_opener,
    HTTPCookieProcessor,
    HTTPRedirectHandler,
    ProxyHandler,
    Request,
    urlopen,
)
import uuid

ROOT = Path(__file__).resolve().parents[1]
GATEWAY = "http://127.0.0.1:8080"
ISSUER = "http://auth.swiftride.test:8180"
LOCAL = "http://127.0.0.1:8180"
OIDC = "/realms/swiftride/protocol/openid-connect"
CALLBACK = "http://127.0.0.1:8976/callback"
PICKUP = {"latitude": 10.7769, "longitude": 106.7009, "label": "Pickup"}
DESTINATION = {"latitude": 10.787, "longitude": 106.713, "label": "Destination"}


class LoginForm(HTMLParser):
    action = None

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "form" and values.get("id") == "kc-form-login":
            self.action = values["action"]


class ReceivedCode(Exception):
    pass


class Callback(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if newurl.split("?", 1)[0] == CALLBACK:
            raise ReceivedCode(newurl)
        if newurl.startswith(ISSUER + "/"):
            newurl = LOCAL + newurl[len(ISSUER) :]
        if not newurl.startswith(LOCAL + "/"):
            raise ValueError("Unexpected OIDC redirect")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def token(fields):
    with urlopen(LOCAL + OIDC + "/token", urlencode(fields).encode(), timeout=10) as response:
        return json.load(response)["access_token"]


def login(username, password):
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest())
    state = secrets.token_urlsafe(24)
    params = dict(
        client_id="swiftride-postman",
        redirect_uri=CALLBACK,
        scope="openid",
        state=state,
        response_type="code",
        code_challenge_method="S256",
        code_challenge=challenge.rstrip(b"=").decode(),
    )
    browser = build_opener(ProxyHandler({}), HTTPCookieProcessor(CookieJar()), Callback())
    headers = {"Host": urlsplit(ISSUER).netloc}
    with browser.open(
        Request(LOCAL + OIDC + "/auth?" + urlencode(params), headers=headers), timeout=10
    ) as response:
        form = LoginForm()
        form.feed(response.read().decode())
    assert form.action and form.action.startswith((LOCAL + "/", ISSUER + "/")), "No login form"
    try:
        with browser.open(
            Request(
                form.action.replace(ISSUER, LOCAL, 1),
                urlencode(dict(username=username, password=password)).encode(),
                headers=headers,
            ),
            timeout=10,
        ):
            raise RuntimeError("Keycloak did not return an authorization code")
    except ReceivedCode as redirect:
        query = parse_qs(urlsplit(str(redirect)).query)
        assert query["state"] == [state]
        return token(
            dict(
                client_id="swiftride-postman",
                code=query["code"][0],
                grant_type="authorization_code",
                redirect_uri=CALLBACK,
                code_verifier=verifier,
            )
        )


def api(method, path, actor, body=None, expected=200, key=None, correlation=None):
    headers = {"Authorization": "Bearer " + actor, "Content-Type": "application/json"}
    if key:
        headers["Idempotency-Key"] = key
    if correlation:
        headers["CorrelationId"] = correlation
    request = Request(
        GATEWAY + path, None if body is None else json.dumps(body).encode(), headers, method=method
    )
    deadline = time.monotonic() + 65
    while True:
        try:
            response = urlopen(request, timeout=15)
        except HTTPError as error:
            response = error
        with response:
            payload = response.read()
            if response.status == 429 and time.monotonic() < deadline:
                time.sleep(2)
                continue
            assert response.status == expected, (
                method,
                path,
                expected,
                response.status,
                payload[:500],
            )
            return json.loads(payload) if payload else None


def book(rider, method="Card", card="card-ok", pickup=PICKUP, destination=DESTINATION):
    key = str(uuid.uuid4())
    body = dict(pickup=pickup, destination=destination, method=method)
    if method == "Card":
        body["paymentToken"] = card
    trip = api("POST", "/trips", rider, body, 201, key, key)
    assert trip["status"] == "Requested"
    assert api("POST", "/trips", rider, body, key=key)["id"] == trip["id"]
    return trip


def wait(trip, rider, statuses, timeout=80):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        trip = api("GET", "/trips/" + trip["id"], rider)
        if trip["status"] in statuses:
            return trip
        time.sleep(0.6)
    raise AssertionError((trip["id"], trip["status"], statuses))


def move(trip, action, actor, expected=200):
    body = {"expectedVersion": trip["version"]}
    if action == "confirm-quote":
        body["quoteId"] = trip["quote"]["id"]
    return api("POST", f"/trips/{trip['id']}/{action}", actor, body, expected)


def offer(trip, rider):
    trip = wait(trip, rider, ["AwaitingRiderConfirmation"])
    move(trip, "confirm-quote", rider)
    return wait(trip, rider, ["AwaitingDriverAcceptance"])


def pickup(trip, rider, drivers):
    trip = offer(trip, rider)
    driver = drivers[trip["driverId"]]
    other = next(value for key, value in drivers.items() if key != trip["driverId"])
    move(trip, "driver-accept", other, 403)
    move(trip, "driver-accept", driver)
    trip = wait(trip, rider, ["DriverAccepted"])
    move(trip, "en-route", other, 403)
    ride = move(trip, "en-route", driver)
    move(trip, "en-route", driver, 409)
    return move(ride, "pickup", driver)


def compose(*args):
    subprocess.run(["docker", "compose", *args], cwd=ROOT, check=True)


def sql(statement):
    result = subprocess.run(
        [
            "docker",
            "compose",
            "exec",
            "-T",
            "postgres",
            "psql",
            "-X",
            "-U",
            "payment_app",
            "-d",
            "PayDB",
            "-v",
            "ON_ERROR_STOP=1",
            "-Atq",
        ],
        cwd=ROOT,
        input=statement,
        capture_output=True,
        text=True,
        check=True,
    )
    return result.stdout.strip()


@contextmanager
def fail_one_wallet_insert(trip_id):
    # nextval survives rollback, so just the first capture encounters a serialization failure.
    trip_id = uuid.UUID(trip_id)
    name = "accept_" + trip_id.hex
    sql(f"""
        BEGIN;
        CREATE SEQUENCE {name};
        CREATE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF nextval('{name}') = 1 THEN
                RAISE serialization_failure USING MESSAGE = 'Acceptance transient failure';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER {name} BEFORE INSERT ON "Payments" FOR EACH ROW
            WHEN (NEW."Id" = '{trip_id}') EXECUTE FUNCTION {name}();
        COMMIT;
    """)
    try:
        yield
        assert sql(f"SELECT last_value FROM {name}") == "2"
    finally:
        sql(f'DROP TRIGGER {name} ON "Payments"; DROP FUNCTION {name}(); DROP SEQUENCE {name};')


def run_collection(newman, rider, driver):
    with tempfile.TemporaryDirectory() as private:
        env = json.loads((ROOT / "postman/SwiftRide.local.postman_environment.json").read_text())
        for field in env["values"]:
            if field["key"] in ("riderToken", "driverToken"):
                field["value"] = rider if field["key"] == "riderToken" else driver
        path = Path(private) / "environment.json"
        path.write_text(json.dumps(env))
        subprocess.run(
            [
                newman,
                "run",
                "postman/SwiftRide.postman_collection.json",
                "-e",
                str(path),
                "--bail",
                "--timeout",
                "180000",
            ],
            cwd=ROOT,
            check=True,
        )


def redeliver(trip, card, correlation, settings):
    name = "SwiftRide.Core:RideCompleted"
    msg = dict(
        eventId=str(uuid.uuid4()),
        tripId=trip["id"],
        riderId=trip["riderId"],
        driverId=trip["driverId"],
        method=trip["method"],
        amount=trip["quote"]["amount"],
        paymentToken=card,
        correlationId=correlation,
        attempt=1,
    )
    envelope = dict(messageId=str(uuid.uuid4()), messageType=["urn:message:" + name], message=msg)
    payload = dict(
        properties={"content_type": "application/vnd.masstransit+json"},
        routing_key="",
        payload=json.dumps(envelope),
        payload_encoding="string",
    )
    auth = (settings.get("RABBIT_USER") or "swiftride") + ":" + settings["RABBIT_PASSWORD"]
    headers = {
        "Content-Type": "application/json",
        "Authorization": "Basic " + base64.b64encode(auth.encode()).decode(),
    }
    req = Request(
        "http://127.0.0.1:15672/api/exchanges/%2F/" + name + "/publish",
        json.dumps(payload).encode(),
        headers,
    )
    with urlopen(req, timeout=10) as response:
        assert json.load(response)["routed"]


def verify_payment(trip, rider, service, card, opening):
    path = "/payments/" + trip["id"]
    amount = trip["quote"]["amount"]
    body = dict(
        tripId=trip["id"],
        riderId=trip["riderId"],
        amount=amount,
        method=trip["method"],
        paymentToken=card,
        driverId=trip["driverId"],
        attempt=1,
    )
    key = "trip:" + trip["id"]
    assert api("GET", path, rider)["status"] == "Settled"
    api("POST", "/payments", rider, body, 403, key + ":charge")
    for _ in range(2):
        assert api("POST", "/payments", service, body, key=key + ":charge")["status"] == "Settled"
    api("POST", "/payments", service, dict(body, amount=amount + 1), 409, key + ":charge")
    assert len(api("GET", path + "/ledger", rider)) == 1
    wallet = trip["method"] == "Wallet"
    assert api("GET", "/wallets/me", rider)["available"] == opening - (amount if wallet else 0)
    api("POST", path + "/refund", rider, {"reason": "Acceptance reversal"}, 403, key + ":refund")
    for _ in range(2):
        result = api(
            "POST",
            path + "/refund",
            service,
            {"reason": "Acceptance reversal"},
            key=key + ":refund",
        )
        assert result["status"] == "Refunded"
    lines = api("GET", path + "/ledger", rider)
    assert sorted(entry["kind"] for entry in lines) == ["Charge", "Refund"]
    accounts = {}
    for entry in lines:
        total = 0
        for line in entry["lines"]:
            delta = line["amount"] * (1 if line["side"] == "Debit" else -1)
            accounts[line["account"]] = accounts.get(line["account"], 0) + delta
            total += delta
        assert total == 0
    assert all(value == 0 for value in accounts.values())
    assert api("GET", "/wallets/me", rider)["available"] == opening
    move(wait(trip, rider, ["PaymentReview"]), "cancel", rider, 409)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--newman", default="newman")
    args = parser.parse_args()
    settings = dict(
        line.split("=", 1)
        for line in (ROOT / ".env").read_text().splitlines()
        if "=" in line and not line.startswith("#")
    )
    rider, poor = (login(f"rider{i}", settings["RIDER_PASSWORD"]) for i in (1, 2))
    drivers = {
        f"22222222-2222-2222-2222-{i:012}": login(f"driver{i}", settings["DRIVER_PASSWORD"])
        for i in range(1, 5)
    }
    service = token(
        dict(
            client_id="swiftride-service",
            grant_type="client_credentials",
            client_secret=settings["SERVICE_CLIENT_SECRET"],
        )
    )
    run_collection(args.newman, rider, next(iter(drivers.values())))
    rejected = wait(book(rider), rider, ["AwaitingRiderConfirmation"])
    assert move(rejected, "cancel", rider)["status"] == "Cancelled"
    far = dict(PICKUP, latitude=0, longitude=0)
    wait(book(rider, pickup=far, destination=dict(far, longitude=0.01)), rider, ["Failed"])
    trip = book(rider)
    seen = set()
    for i in range(4):
        trip = offer(trip, rider)
        assert trip["driverId"] not in seen
        seen.add(trip["driverId"])
        trip = move(trip, "driver-reject", drivers[trip["driverId"]])
        assert trip["status"] == ("Failed" if i == 3 else "Requested")
    print("PASS cancellation, no candidates and four distinct driver rejections", flush=True)

    quotes = [wait(book(person), person, ["AwaitingRiderConfirmation"]) for person in (rider, poor)]
    assert quotes[0]["driverId"] == quotes[1]["driverId"]
    with ThreadPoolExecutor(2) as pool:
        list(
            pool.map(
                lambda pair: move(pair[0], "confirm-quote", pair[1]), zip(quotes, (rider, poor))
            )
        )
    results = [
        wait(q, person, ["AwaitingDriverAcceptance", "AwaitingRiderConfirmation"])
        for q, person in zip(quotes, (rider, poor))
    ]
    assert sorted(t["status"] for t in results) == [
        "AwaitingDriverAcceptance",
        "AwaitingRiderConfirmation",
    ]
    assert results[0]["driverId"] != results[1]["driverId"]
    for t, person in zip(results, (rider, poor)):
        move(t, "cancel", person)
    print("PASS concurrent riders reserve one driver atomically", flush=True)

    for person, method, card in (
        (poor, "Wallet", None),
        (rider, "Card", "card-decline"),
        (rider, "Card", "card-timeout"),
        (rider, "Wallet", None),
    ):
        opening = api("GET", "/wallets/me", person)["available"]
        trip = pickup(book(person, method, card), person, drivers)
        driver = drivers[trip["driverId"]]
        assert api("GET", "/wallets/me", person)["available"] == opening
        recovery = method == "Wallet" and person == rider
        if recovery:
            with fail_one_wallet_insert(trip["id"]):
                compose("stop", "rabbitmq")
                try:
                    move(trip, "dropoff", driver)
                    wait(trip, rider, ["PaymentPending"])
                    compose("restart", "trip", "payment")
                finally:
                    compose("up", "-d", "--no-build", "--wait", "gateway")
                wait(trip, rider, ["Paid"])
        else:
            move(trip, "dropoff", driver)
        if person == poor or card == "card-decline":
            trip = wait(trip, person, ["PaymentReview"])
            move(trip, "cancel", person, 409)
            assert api("GET", "/payments/" + trip["id"], person)["status"] == "Declined"
            assert not api("GET", "/payments/" + trip["id"] + "/ledger", person)
            assert api("GET", "/wallets/me", person)["available"] == opening
            continue
        if card == "card-timeout":
            trip = wait(trip, person, ["PaymentReview"])
            move(trip, "cancel", person, 409)
            move(trip, "retry-payment", service)
        trip = wait(trip, person, ["Paid"])
        api("GET", "/payments/" + trip["id"], poor, expected=403)
        if recovery:
            redeliver(trip, card, str(uuid.uuid4()), settings)
        verify_payment(trip, person, service, card, opening)
        print("PASS", method, "capture, replay and refund", trip["id"], flush=True)
    # Deliver a confirmed, mismatched capture while the completion event waits in Trip's outbox.
    trip = pickup(book(poor), poor, drivers)
    path = "/payments/" + trip["id"]
    amount = trip["quote"]["amount"] + 1
    charge = dict(
        tripId=trip["id"],
        riderId=trip["riderId"],
        driverId=trip["driverId"],
        amount=amount,
        method="Card",
        paymentToken="card-ok",
    )
    compose("stop", "rabbitmq")
    try:
        move(trip, "dropoff", drivers[trip["driverId"]])
        api("POST", "/payments", service, charge, key=f"trip:{trip['id']}:charge")
    finally:
        compose("up", "-d", "--wait", "rabbitmq")
    end = time.monotonic() + 60
    while time.monotonic() < end and api("GET", path, poor)["status"] != "Refunded":
        time.sleep(0.5)
    assert api("GET", path, poor)["status"] == "Refunded"
    assert sorted(j["kind"] for j in api("GET", path + "/ledger", poor)) == ["Charge", "Refund"]
    trip = wait(trip, poor, ["PaymentReview"])
    move(trip, "cancel", poor, 409)
    print("PASS saga reversed one mismatched capture", trip["id"], flush=True)
    print("PASS insufficient balance, card decline, retry exhaustion, replay and broker recovery")


if __name__ == "__main__":
    main()
