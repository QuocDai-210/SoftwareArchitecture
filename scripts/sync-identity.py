#!/usr/bin/env python3
"""Update the existing realm without recreating users, subjects, or password hashes."""

import json
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


def settings():
    return dict(
        line.split("=", 1)
        for line in (ROOT / ".env").read_text().splitlines()
        if line and not line.startswith("#") and "=" in line
    )


def synchronize():
    env = settings()
    base = "http://localhost:8081"
    token_request = urllib.request.Request(
        base + "/realms/master/protocol/openid-connect/token",
        data=urllib.parse.urlencode(
            {
                "grant_type": "password",
                "client_id": "admin-cli",
                "username": "admin",
                "password": env["KEYCLOAK_ADMIN_PASSWORD"],
            }
        ).encode(),
    )
    with urllib.request.urlopen(token_request, timeout=15) as response:
        token = json.load(response)["access_token"]

    def api(method, path, body=None):
        request = urllib.request.Request(
            base + "/admin/realms/swiftride" + path,
            method=method,
            data=json.dumps(body).encode() if body is not None else None,
            headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
        )
        with urllib.request.urlopen(request, timeout=15) as response:
            data = response.read()
            return json.loads(data) if data else None

    source = json.loads((ROOT / "deploy/keycloak/swiftride-realm.json").read_text())
    realm = api("GET", "")
    for key in (
        "displayName",
        "loginTheme",
        "internationalizationEnabled",
        "supportedLocales",
        "defaultLocale",
        "registrationAllowed",
        "registrationEmailAsUsername",
        "loginWithEmailAllowed",
        "passwordPolicy",
    ):
        realm[key] = source[key]
    api("PUT", "", realm)
    roles = {role["name"]: role for role in api("GET", "/roles")}
    for name in ("user", "driver", "ops", "service"):
        if name not in roles:
            api("POST", "/roles", {"name": name})
            roles[name] = api("GET", "/roles/" + name)
    default = realm["defaultRole"]["name"]
    api("POST", "/roles/" + default + "/composites", [roles["user"]])

    def users_with_role(name):
        first = 0
        while True:
            batch = api("GET", f"/roles/{name}/users?first={first}&max=100")
            yield from batch
            if len(batch) < 100:
                return
            first += len(batch)

    migrated = 0
    if "rider" in roles:
        for user in users_with_role("rider"):
            api("POST", f"/users/{user['id']}/role-mappings/realm", [roles["user"]])
            migrated += 1

    # Driver, Ops, and machine identities must not inherit public User permissions.
    for name in ("driver", "ops", "service"):
        for user in users_with_role(name):
            mappings = api("GET", f"/users/{user['id']}/role-mappings/realm")
            remove = [r for r in mappings if r["name"] in {"user", "rider", default}]
            if remove:
                api("DELETE", f"/users/{user['id']}/role-mappings/realm", remove)

    clients = api("GET", "/clients?clientId=swiftride-ops")
    ops_client = next(c for c in source["clients"] if c["clientId"] == "swiftride-ops")
    ops_client["secret"] = env["OPS_CLIENT_SECRET"]
    if not clients:
        api("POST", "/clients", ops_client)
        clients = api("GET", "/clients?clientId=swiftride-ops")
    client_id = clients[0]["id"]
    api("PUT", "/clients/" + client_id, ops_client)
    identity = api("GET", f"/clients/{client_id}/service-account-user")
    mappings = api("GET", f"/users/{identity['id']}/role-mappings/realm")
    if mappings:
        api("DELETE", f"/users/{identity['id']}/role-mappings/realm", mappings)
    management = api("GET", "/clients?clientId=realm-management")[0]["id"]
    permissions = [
        api("GET", f"/clients/{management}/roles/{name}")
        for name in ("manage-users", "view-users", "query-users", "view-realm")
    ]
    api("POST", f"/users/{identity['id']}/role-mappings/clients/{management}", permissions)

    for legacy in api("GET", "/clients?clientId=swiftride-check"):
        legacy["enabled"] = False
        api("PUT", "/clients/" + legacy["id"], legacy)

    profile = api("GET", "/users/profile")
    if not any(a["name"] == "swiftrideApplicationId" for a in profile["attributes"]):
        profile["attributes"].append(
            {
                "name": "swiftrideApplicationId",
                "multivalued": False,
                "permissions": {"view": ["admin"], "edit": ["admin"]},
                "validations": {"length": {"max": 36}},
            }
        )
        api("PUT", "/users/profile", profile)
    print(
        f"Identity synchronized: User registration, isolated roles, Driver provisioning; {migrated} legacy User identities retained."
    )


if __name__ == "__main__":
    try:
        synchronize()
    except urllib.error.HTTPError as error:
        raise SystemExit(
            f"Identity synchronization failed: HTTP {error.code}. Existing identities retained."
        ) from None
    except urllib.error.URLError:
        raise SystemExit("Cannot connect to Keycloak at http://localhost:8081.") from None
