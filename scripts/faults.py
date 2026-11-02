#!/usr/bin/env python3
"""Actual local container faults. Run after verify.sh on demo data only."""

import argparse
import subprocess
import time
import uuid
from demo import ROOT, action, api, assert_ledger, compose, prepare, publish_duplicate, wait
from replay import replay


def broker_outage():
    driver, trip = prepare()
    try:
        compose("stop", "rabbitmq")
        action(driver, trip, "dropoff")
        pending = wait(trip["id"], "PaymentPending", 20)
        assert pending["status"] != "Paid"
        count = subprocess.check_output(
            [
                "docker",
                "compose",
                "exec",
                "-T",
                "sqlserver",
                "bash",
                "-c",
                'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d TripDB -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM OutboxMessage;"',
            ],
            cwd=ROOT,
            text=True,
        )
        assert (
            int(count.strip()) > 0
        ), "Payment event must be persisted in SQL Outbox while broker is down"
        compose("restart", "trip")
        wait(trip["id"], "PaymentPending", 20)
    finally:
        compose("start", "rabbitmq")
    wait(trip["id"], "Paid")
    assert_ledger(trip)
    print(
        "PASS broker outage and Trip restart: SQL Outbox persists event, eventual settlement once"
    )


def duplicate():
    driver, trip = prepare()
    action(driver, trip, "dropoff")
    wait(trip["id"], "Paid")
    message_id = str(uuid.uuid4())
    publish_duplicate(trip, message_id)
    publish_duplicate(trip, message_id)
    publish_duplicate(trip)
    time.sleep(5)
    assert_ledger(trip)
    print("PASS duplicate transport message and independent duplicate business request: one charge")


def restart_trip():
    driver, trip = prepare()
    compose("restart", "trip")
    trip = wait(trip["id"], "PickedUp")
    action(driver, trip, "dropoff")
    wait(trip["id"], "Paid")
    assert_ledger(trip)
    print("PASS trip restart: persisted lifecycle and payment intact")


def restart_payment():
    driver, trip = prepare("demo-unknown")
    action(driver, trip, "dropoff")
    try:
        deadline = time.monotonic() + 20
        while True:
            captured = subprocess.check_output(
                [
                    "docker",
                    "compose",
                    "exec",
                    "-T",
                    "postgres",
                    "psql",
                    "-U",
                    "payment",
                    "-d",
                    "PayDB",
                    "-Atc",
                    f"""SELECT count(*) FROM "ProviderOperations" WHERE "Key" = 'trip:{trip['id']}:charge' AND "UnknownInjected";""",
                ],
                cwd=ROOT,
                text=True,
            )
            if captured.strip() == "1":
                compose("kill", "-s", "SIGKILL", "payment")
                journals = subprocess.check_output(
                    [
                        "docker",
                        "compose",
                        "exec",
                        "-T",
                        "postgres",
                        "psql",
                        "-U",
                        "payment",
                        "-d",
                        "PayDB",
                        "-Atc",
                        f"""SELECT count(*) FROM "Journals" WHERE "PaymentId" = '{trip['id']}';""",
                    ],
                    cwd=ROOT,
                    text=True,
                )
                assert (
                    journals.strip() == "0"
                ), "Crash must occur after provider capture but before ledger commit"
                break
            assert time.monotonic() < deadline, "Provider capture was not persisted before crash"
            time.sleep(0.1)
    finally:
        compose("start", "payment")
    wait(trip["id"], "Paid")
    assert_ledger(trip)
    print("PASS payment restart and provider capture timeout: no duplicate charge")


def dead_letter():
    driver, trip = prepare("demo-unavailable")
    action(driver, trip, "dropoff")
    wait(trip["id"], "PaymentReview")
    api("ops1", f"/payments/{trip['id']}/demo-recover", {}, "POST")
    replay("ride-completed_error")
    wait(trip["id"], "Paid")
    assert_ledger(trip)
    print("PASS exhausted retries, error queue, provider recovery, confirmed replay")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "scenario",
        nargs="?",
        default="all",
        choices=["all", "broker", "duplicate", "trip-restart", "payment-restart", "dead-letter"],
    )
    scenarios = {
        "broker": broker_outage,
        "duplicate": duplicate,
        "trip-restart": restart_trip,
        "payment-restart": restart_payment,
        "dead-letter": dead_letter,
    }
    choice = parser.parse_args().scenario
    for name, test in scenarios.items():
        if choice in ("all", name):
            print("Running fault scenario:", name, flush=True)
            test()
