#!/usr/bin/env python3
"""Generate local-only secrets without printing credentials."""

import pathlib
import secrets
import os

root = pathlib.Path(__file__).resolve().parents[1]
path = root / ".env"
if path.exists():
    raise SystemExit(".env already exists; retained. Read it locally for demo passwords.")
lines = []
for line in (root / ".env.example").read_text().splitlines():
    if line and not line.startswith("#"):
        key, value = line.split("=", 1)
        if not value:
            value = "Aa1!" + secrets.token_hex(24)
        lines.append(f"{key}={value}")
fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
with os.fdopen(fd, "w") as output:
    output.write("\n".join(lines) + "\n")
print("Created .env with mode 0600. Read locally; do not commit it.")
