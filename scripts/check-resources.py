#!/usr/bin/env python3
"""Check every Compose service, including optional profiles, against the RAM budget."""

import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BUDGET = 12_000_000_000


def check(services):
    total = 0
    for name, service in services.items():
        limit = int(service.get("mem_limit", 0))
        if limit <= 0 or int(service.get("memswap_limit", 0)) != limit:
            raise ValueError(f"{name}: require a RAM limit and disable container swap")
        total += limit
    if total > BUDGET:
        raise ValueError(f"Compose RAM limits exceed 12 GB: {total:,} bytes")
    return total


if __name__ == "__main__":
    assert check({"api": {"mem_limit": 1024, "memswap_limit": 1024}}) == 1024
    for invalid in (
        {"api": {}},
        {"api": {"mem_limit": 1024, "memswap_limit": 2048}},
        {"api": {"mem_limit": BUDGET + 1, "memswap_limit": BUDGET + 1}},
    ):
        try:
            check(invalid)
        except ValueError:
            pass
        else:
            raise AssertionError("Invalid resource limits accepted")
    config = json.loads(
        subprocess.check_output(
            ["docker", "compose", "--profile", "*", "config", "--format", "json"], cwd=ROOT
        )
    )
    total = check(config["services"])
    assert config["services"]["observability"]["profiles"] == ["observability"]
    print(f"RAM limits passed: {len(config['services'])} services, {total / 1e9:.2f}/12 GB.")
