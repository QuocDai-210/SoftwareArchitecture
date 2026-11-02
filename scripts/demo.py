"""Local demo helpers. Stdlib only; credentials never printed."""
import base64
import json
import os
import pathlib
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]
for line in (ROOT / '.env').read_text().splitlines():
    if line and not line.startswith('#'):
        key, value = line.split('=', 1)
        os.environ.setdefault(key, value)
BASE = os.environ.get('GATEWAY_URL', 'http://localhost:8080')
IDENTITY = os.environ.get('IDENTITY_URL', 'http://localhost:8081')
PICKUP = {'latitude': 10.7769, 'longitude': 106.7009, 'label': 'Nhà hát Thành phố'}
DESTINATION = {'latitude': 10.7599, 'longitude': 106.6822, 'label': 'Đại học Sài Gòn'}
TOKENS = {}


def request(url, method='GET', body=None, headers=None):
    payload = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=payload, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            data = response.read()
            return response.status, json.loads(data) if data else None
    except urllib.error.HTTPError as error:
        raise RuntimeError(f'HTTP {error.code} for {method} {urllib.parse.urlparse(url).path}') from None


def token(user):
    kind = 'DRIVER' if user.startswith('driver') else 'OPS' if user.startswith('ops') else 'RIDER'
    payload = urllib.parse.urlencode({'grant_type': 'password', 'client_id': 'swiftride-check',
        'username': user, 'password': os.environ[kind + '_PASSWORD']}).encode()
    req = urllib.request.Request(IDENTITY + '/realms/swiftride/protocol/openid-connect/token',
        data=payload, headers={'Content-Type': 'application/x-www-form-urlencoded'})
    with urllib.request.urlopen(req, timeout=10) as response:
        return json.load(response)['access_token']


def api(user, path, body=None, method=None, key=None):
    method = method or ('POST' if body is not None else 'GET')
    if user not in TOKENS:
        TOKENS[user] = token(user)
    headers = {'Authorization': 'Bearer ' + TOKENS[user], 'Content-Type': 'application/json'}
    if key:
        headers['Idempotency-Key'] = key
    try:
        return request(BASE + path, method, body, headers)[1]
    except RuntimeError as error:
        if str(error).startswith('HTTP 401'):
            TOKENS[user] = token(user)
            headers['Authorization'] = 'Bearer ' + TOKENS[user]
            return request(BASE + path, method, body, headers)[1]
        raise


def broker(path, method='GET', body=None):
    auth = base64.b64encode((os.environ['RABBIT_USER'] + ':' + os.environ['RABBIT_PASSWORD']).encode()).decode()
    return request('http://localhost:15672/api/' + path, method, body,
        {'Authorization': 'Basic ' + auth, 'Content-Type': 'application/json'})[1]


def compose(*args):
    subprocess.run(['docker', 'compose', *args], cwd=ROOT, check=True)


def wait(trip_id, status, timeout=120):
    end = time.monotonic() + timeout
    trip = None
    while time.monotonic() < end:
        try:
            trip = api('rider1', '/trips/' + trip_id)
            if trip['status'] == status:
                return trip
        except (RuntimeError, OSError):
            pass
        time.sleep(1)
    raise RuntimeError(f"Trip {trip_id}: expected {status}, actual {trip['status'] if trip else 'unavailable'}")


def action(user, trip, command):
    return api(user, f"/trips/{trip['id']}/{command}", {'expectedVersion': trip['version']})


def prepare(card='demo-success'):
    for i in range(1, 5):
        api(f'driver{i}', '/drivers/me/location', {'latitude': PICKUP['latitude'] + i * 0.0001, 'longitude': PICKUP['longitude']}, 'PUT')
        api(f'driver{i}', '/drivers/me/availability', {'available': True}, 'PUT')
    trip = api('rider1', '/trips', {'pickup': PICKUP, 'destination': DESTINATION,
        'method': 'Card', 'paymentToken': card}, key=str(uuid.uuid4()))
    trip = wait(trip['id'], 'AwaitingRiderConfirmation')
    api('rider1', f"/trips/{trip['id']}/confirm-quote", {'expectedVersion': trip['version'], 'quoteId': trip['quote']['id']})
    trip = wait(trip['id'], 'AwaitingDriverAcceptance')
    driver = 'driver' + str(int(trip['driverId'][-12:]))
    action(driver, trip, 'driver-accept')
    trip = wait(trip['id'], 'DriverAccepted')
    trip = action(driver, trip, 'en-route')
    trip = action(driver, trip, 'pickup')
    return driver, trip


def assert_ledger(trip, count=1):
    rows = api('rider1', f"/payments/{trip['id']}/ledger")
    assert len(rows) == count, f"Expected {count} journals, got {len(rows)}"
    for row in rows:
        assert sum(x['amount'] if x['side'] == 'Debit' else -x['amount'] for x in row['lines']) == 0


def publish_duplicate(trip, message_id=None, card='demo-success'):
    message_id = message_id or str(uuid.uuid4())
    envelope = {'messageId': message_id, 'conversationId': str(uuid.uuid4()), 'correlationId': str(uuid.uuid4()),
        'messageType': ['urn:message:SwiftRide.Core:RideCompleted'], 'headers': {'SchemaVersion': 1},
        'message': {'eventId': message_id, 'tripId': trip['id'], 'riderId': trip['riderId'],
            'amount': trip['quote']['amount'], 'method': 'Card', 'paymentToken': card, 'correlationId': str(uuid.uuid4())}}
    result = broker('exchanges/%2F/SwiftRide.Core%3ARideCompleted/publish', 'POST', {
        'properties': {'content_type': 'application/vnd.masstransit+json', 'delivery_mode': 2},
        'routing_key': '', 'payload': json.dumps(envelope), 'payload_encoding': 'string'})
    assert result['routed'], 'Duplicate message was not routed'
