"""Update the registered web callback on an existing local Keycloak volume."""

import socket
import json
import os
import secrets
import subprocess
from urllib.parse import urlencode
from urllib.request import Request, urlopen

base = "http://localhost:8180"
client = "localhost-migration-" + secrets.token_hex(8)
secret = secrets.token_urlsafe(48)
compose = ["docker", "compose"]


def request(path, token=None, method="GET", data=None):
    headers = {"Authorization": "Bearer " + token} if token else {}
    if isinstance(data, dict):
        headers["Content-Type"] = "application/json"
        data = json.dumps(data).encode()
    with urlopen(Request(base + path, data, headers, method=method), timeout=15) as response:
        body = response.read()
        return json.loads(body) if body else None


def migrate():
    subprocess.run(compose + ["stop", "keycloak"], check=True)
    try:
        subprocess.run(
            compose
            + [
                "run",
                "--rm",
                "--no-deps",
                "-e",
                "MIGRATION_SECRET",
                "keycloak",
                "bootstrap-admin",
                "service",
                "--db",
                "dev-file",
                "--client-id",
                client,
                "--client-secret:env",
                "MIGRATION_SECRET",
                "--no-prompt",
            ],
            env={**os.environ, "MIGRATION_SECRET": secret},
            check=True,
        )
    finally:
        subprocess.run(compose + ["up", "-d", "--wait", "keycloak"], check=True)
    token = request(
        "/realms/master/protocol/openid-connect/token",
        method="POST",
        data=urlencode(
            dict(grant_type="client_credentials", client_id=client, client_secret=secret)
        ).encode(),
    )["access_token"]
    admin = "/admin/realms/master/clients"
    temporary = request(admin + "?clientId=" + client, token)[0]["id"]
    try:
        path = "/admin/realms/swiftride/clients"
        record = request(path + "?clientId=swiftride-postman", token)[0]
        old = f"http://{socket.gethostbyname('localhost')}:8976/callback"
        uris = ["http://localhost:8976/callback" if u == old else u for u in record["redirectUris"]]
        if uris != record["redirectUris"]:
            request(
                path + "/" + record["id"], token, "PUT", {"redirectUris": list(dict.fromkeys(uris))}
            )
    finally:
        request(admin + "/" + temporary, token, "DELETE")


if __name__ == "__main__":
    migrate()
