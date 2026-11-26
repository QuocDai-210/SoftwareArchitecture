#!/usr/bin/env python3
"""Create local secrets and append missing settings without replacing existing values."""

from contextlib import contextmanager
import os
from pathlib import Path
import secrets
import sys
import tempfile

if os.name == "nt":
    import msvcrt
else:
    import fcntl


@contextmanager
def locked(output):
    output.seek(0)
    if os.name == "nt":
        msvcrt.locking(output.fileno(), msvcrt.LK_LOCK, 1)
    else:
        fcntl.flock(output, fcntl.LOCK_EX)
    try:
        yield
    finally:
        output.flush()
        output.seek(0)
        if os.name == "nt":
            msvcrt.locking(output.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            fcntl.flock(output, fcntl.LOCK_UN)


GENERATED = {
    "POSTGRES_ADMIN_PASSWORD",
    "TRIP_DB_PASSWORD",
    "MONGO_ROOT_PASSWORD",
    "MATCH_DB_PASSWORD",
    "PAY_DB_PASSWORD",
    "RABBIT_PASSWORD",
    "SERVICE_CLIENT_SECRET",
    "RIDER_PASSWORD",
    "DRIVER_PASSWORD",
    "GRAFANA_PASSWORD",
}


def setup(root):
    path = root / ".env"
    with path.open("a+", encoding="utf-8") as output, locked(output):
        os.chmod(path, 0o600)
        output.seek(0)
        original = output.read()
        keys = {line.split("=", 1)[0] for line in original.splitlines() if "=" in line}
        additions = []
        for line in (root / ".env.example").read_text(encoding="utf-8").splitlines():
            if not line or line.startswith("#"):
                continue
            key, value = line.split("=", 1)
            if key in keys:
                continue
            if key in GENERATED and not value:
                value = "Aa1!" + secrets.token_hex(24)
            additions.append(f"{key}={value}")
        if additions:
            output.write(
                ("\n" if original and not original.endswith("\n") else "")
                + "\n".join(additions)
                + "\n"
            )
        return len(additions)


def check():
    with tempfile.TemporaryDirectory(prefix="swiftride-setup-") as directory:
        root = Path(directory)
        (root / ".env.example").write_text(
            "SERVICE_CLIENT_SECRET=\nOTEL_SDK_DISABLED=true\n", encoding="utf-8"
        )
        (root / ".env").write_text("# retained\nEXISTING=keep", encoding="utf-8")
        assert setup(root) == 2
        result = (root / ".env").read_text(encoding="utf-8")
        assert result.startswith("# retained\nEXISTING=keep\n")
        assert "SERVICE_CLIENT_SECRET=Aa1!" in result
        assert result.endswith("OTEL_SDK_DISABLED=true\n")
        assert setup(root) == 0
        assert (root / ".env").read_text(encoding="utf-8") == result
        if os.name != "nt":
            assert (root / ".env").stat().st_mode & 0o777 == 0o600
    print("Setup checks passed: existing values retained and configuration defaults retained.")


if __name__ == "__main__":
    if "--check" in sys.argv:
        check()
    else:
        count = setup(Path(__file__).resolve().parents[1])
        print(f"Added {count} missing settings to .env; existing values retained.")
