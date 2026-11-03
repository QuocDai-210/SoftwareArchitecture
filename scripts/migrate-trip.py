#!/usr/bin/env python3
"""Offline Trip migration using native database tools. Never delete source volumes."""

import argparse
from datetime import datetime, timedelta, timezone
from decimal import Decimal, ROUND_HALF_EVEN
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import tempfile
from uuid import UUID, uuid4

ROOT = Path(__file__).resolve().parents[1]
TABLES = ("UserProfiles", "Trips", "InboxState", "OutboxState", "OutboxMessage")
MIGRATION = "202611030001_PostgreSql"


def compose(*args, data=None):
    result = subprocess.run(
        ["docker", "compose", "--profile", "migration", *args],
        cwd=ROOT,
        input=data,
        capture_output=True,
        check=False,
    )
    if result.returncode:
        raise RuntimeError("Database command failed; no data or credentials printed.")
    return result.stdout


def postgres(sql, database="TripDB", user="swiftride_admin"):
    return (
        compose(
            "exec",
            "-T",
            "postgres",
            "psql",
            "-X",
            "-U",
            user,
            "-d",
            database,
            "-v",
            "ON_ERROR_STOP=1",
            "-qAt",
            data=sql.encode(),
        )
        .decode()
        .strip()
    )


UUID_COLUMNS = {
    "Trips": {"Id", "ReservationId"},
    "InboxState": {"MessageId", "ConsumerId", "LockId"},
    "OutboxState": {"OutboxId", "LockId"},
    "OutboxMessage": {
        "InboxMessageId",
        "InboxConsumerId",
        "OutboxId",
        "MessageId",
        "ConversationId",
        "CorrelationId",
        "InitiatorId",
        "RequestId",
    },
}


def canonical(rows, table=None):
    def value(key, item):
        if item is not None and key in UUID_COLUMNS.get(table, set()):
            return str(UUID(item))
        timestamps = {
            "Created",
            "CreatedAt",
            "UpdatedAt",
            "Deadline",
            "NextAttemptAt",
            "Received",
            "ExpirationTime",
            "Consumed",
            "Delivered",
            "SentTime",
            "EnqueueTime",
        }
        if key in timestamps and isinstance(item, str):
            if len(item) >= 19 and item[4:5] == "-" and item[10:11] == "T":
                try:
                    stamp = datetime.fromisoformat(item.replace("Z", "+00:00"))
                    # ponytail: PostgreSQL stores microseconds; retain 100 ns in backups,
                    # add a ticks column only if exact sub-microsecond timestamps are required.
                    fraction = re.search(r"\.(\d+)", item)
                    if fraction:
                        micros = int(
                            (Decimal("0." + fraction[1]) * 1000000).quantize(
                                Decimal(1), rounding=ROUND_HALF_EVEN
                            )
                        )
                        stamp = stamp.replace(microsecond=0) + timedelta(microseconds=micros)
                    return (
                        stamp.replace(tzinfo=stamp.tzinfo or timezone.utc)
                        .astimezone(timezone.utc)
                        .isoformat(timespec="microseconds")
                    )
                except ValueError:
                    pass
        return item

    normalized = [
        {key: value(key, item) for key, item in row.items() if key != "RowVersion"} for row in rows
    ]
    return sorted(json.dumps(row, sort_keys=True, ensure_ascii=False) for row in normalized)


def digest(rows, table=None):
    return hashlib.sha256("\n".join(canonical(rows, table)).encode()).hexdigest()


def ensure_offline():
    running = compose("ps", "--status", "running", "--services").decode().splitlines()
    if set(running) & {"trip", "matching", "payment", "gateway", "web"}:
        raise RuntimeError("Stop application services before migration or verification.")


def export(directory):
    directory.mkdir(mode=0o700, parents=True, exist_ok=False)
    os.chmod(directory, 0o700)
    # Native full backups accompany the logical transfer, including unchanged services.
    backup_name = "swiftride-" + uuid4().hex + ".bak"
    compose(
        "exec",
        "-T",
        "sqlserver",
        "bash",
        "-c",
        'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd '
        '-S localhost -U sa -C -b -Q "BACKUP DATABASE [TripDB] TO DISK = '
        "'/var/opt/mssql/" + backup_name + "' WITH COPY_ONLY, INIT, CHECKSUM\"",
    )
    compose("cp", "sqlserver:/var/opt/mssql/" + backup_name, str(directory / "trip.bak"))
    (directory / "trip.bak").chmod(0o600)
    (directory / "payment.dump").write_bytes(
        compose("exec", "-T", "postgres", "pg_dump", "-U", "swiftride_admin", "-d", "PayDB", "-Fc")
    )
    (directory / "matching.archive").write_bytes(
        compose(
            "exec",
            "-T",
            "mongo",
            "sh",
            "-c",
            'mongodump --username root --password "$MONGO_INITDB_ROOT_PASSWORD" '
            "--authenticationDatabase admin --db MatchDB --archive",
        )
    )
    manifest = {}
    for table in TABLES:
        sql = (
            f"SET NOCOUNT ON; SELECT COALESCE(NULLIF((SELECT * FROM [{table}] "
            "FOR JSON PATH, INCLUDE_NULL_VALUES), ''), '[]');"
        )
        raw = compose(
            "exec",
            "-T",
            "sqlserver",
            "bash",
            "-c",
            'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd '
            "-S localhost -U sa -C -d TripDB -b -y 0 -w 65535 -f 65001",
            data=(sql + "\nGO\n").encode(),
        ).decode()
        lines = raw.splitlines()
        start = next((i for i, line in enumerate(lines) if line.lstrip().startswith("[")), None)
        if start is None:
            raise RuntimeError("SQL Server export contains no JSON: " + table)
        rows = json.loads("".join(lines[start:]).strip())
        (directory / f"{table}.json").write_text(json.dumps(rows, ensure_ascii=False))
        manifest[table] = {"count": len(rows), "sha256": digest(rows, table)}
    (directory / "manifest.json").write_text(json.dumps(manifest, indent=2))
    (directory / "git-head").write_bytes(
        subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT)
    )
    for path in directory.iterdir():
        path.chmod(0o600)
    print("Backups and logical export saved outside repo:", directory)


def source(directory):
    manifest = json.loads((directory / "manifest.json").read_text())
    tables = {}
    for table in TABLES:
        rows = json.loads((directory / f"{table}.json").read_text())
        if manifest[table] != {"count": len(rows), "sha256": digest(rows, table)}:
            raise RuntimeError("Source export checksum mismatch: " + table)
        tables[table] = rows
    return tables


def verify(directory):
    for table, rows in source(directory).items():
        actual = json.loads(
            postgres(f"SELECT COALESCE(json_agg(t), '[]'::json) FROM \"{table}\" t;")
        )
        if canonical(actual, table) != canonical(rows, table):
            raise RuntimeError("Data mismatch: " + table)
        print(f"Verified {table}: {len(rows)} rows, canonical SHA-256 matches.")


def transfer(directory):
    tables = source(directory)
    exists = postgres("SELECT to_regclass('\"Trips\"') IS NOT NULL;") == "t"
    if exists:
        count = sum(int(postgres(f'SELECT COUNT(*) FROM "{table}";')) for table in TABLES)
        if count:
            verify(directory)
            print("Target already matches; no rows overwritten.")
            return
    commands = ["BEGIN; SET LOCAL TIME ZONE 'UTC'; SET LOCAL standard_conforming_strings = on;"]
    if not exists:
        commands.append(
            (
                ROOT
                / "backend/src/TripService/Infrastructure/Persistence/Migrations/InitialSchema.sql"
            ).read_text()
        )
        commands.append(
            'CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, '
            '"ProductVersion" varchar(32) NOT NULL);'
            f"INSERT INTO \"__EFMigrationsHistory\" VALUES ('{MIGRATION}', '10.0.12');"
        )
    for table, rows in tables.items():
        if not rows:
            continue
        payload = json.dumps(
            [{key: value for key, value in row.items() if key != "RowVersion"} for row in rows],
            ensure_ascii=False,
        ).replace("'", "''")
        commands.append(
            f'INSERT INTO "{table}" OVERRIDING SYSTEM VALUE '
            f"SELECT * FROM json_populate_recordset(NULL::\"{table}\", '{payload}');"
        )
        expected = f"json_populate_recordset(NULL::\"{table}\", '{payload}')"
        delimiter = "$verify_" + uuid4().hex + "$"
        while delimiter in payload:
            delimiter = "$verify_" + uuid4().hex + "$"
        commands.append(
            f"DO {delimiter} BEGIN IF EXISTS ("
            f"(SELECT to_jsonb(t) - 'RowVersion' FROM \"{table}\" t "
            f"EXCEPT SELECT to_jsonb(e) - 'RowVersion' FROM {expected} e) UNION ALL "
            f"(SELECT to_jsonb(e) - 'RowVersion' FROM {expected} e "
            f"EXCEPT SELECT to_jsonb(t) - 'RowVersion' FROM \"{table}\" t)) "
            f"THEN RAISE EXCEPTION 'Transfer mismatch: {table}'; END IF; END {delimiter};"
        )
    for table, key in (("InboxState", "Id"), ("OutboxMessage", "SequenceNumber")):
        commands.append(
            f"SELECT setval(pg_get_serial_sequence('\"{table}\"', '{key}'), "
            f'COALESCE(MAX("{key}"), 1), COUNT(*) > 0) FROM "{table}";'
        )
    commands.append("COMMIT;")
    postgres("\n".join(commands), user="trip")
    verify(directory)


def check():
    sql = [
        {
            "Id": "12345678-1234-1234-1234-123456789abc",
            "CreatedAt": "2026-11-03T01:02:03.1234567+00:00",
            "RowVersion": "old",
        }
    ]
    pg = [
        {
            "CreatedAt": "2026-11-03T08:02:03.123457+07:00",
            "Id": "12345678-1234-1234-1234-123456789abc",
            "RowVersion": None,
        }
    ]
    assert canonical(sql) == canonical(pg)
    assert digest(sql) != digest([{**sql[0], "Id": "changed"}])
    assert digest([{**sql[0], "Reason": "2026-11-03T01:02:03Z"}]) != digest(
        [{**sql[0], "Reason": "2026-11-03T01:02:03+00:00"}]
    )
    assert canonical(sql + pg) == canonical(pg + sql)
    with tempfile.TemporaryDirectory(prefix="swiftride-migration-check-") as temp:
        directory = Path(temp)
        manifest = {}
        for table in TABLES:
            (directory / f"{table}.json").write_text(json.dumps(sql))
            manifest[table] = {"count": 1, "sha256": digest(sql, table)}
        (directory / "manifest.json").write_text(json.dumps(manifest))
        assert len(source(directory)) == len(TABLES)
        (directory / "Trips.json").write_text("[]")
        try:
            source(directory)
        except RuntimeError:
            pass
        else:
            raise AssertionError("Corrupted export accepted")
    print("Migration checks passed: UTC, precision, ordering, checksums and tamper rejection.")


if __name__ == "__main__":
    if sys.argv[1:] == ["--check"]:
        check()
        sys.exit(0)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("export", "import", "verify"))
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    directory = args.directory.resolve()
    if directory == ROOT or ROOT in directory.parents:
        parser.error("Backup directory must be outside repo.")
    os.umask(0o077)
    ensure_offline()
    {"export": export, "import": transfer, "verify": verify}[args.action](directory)
