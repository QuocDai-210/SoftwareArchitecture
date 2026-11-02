#!/bin/bash
set -euo pipefail
case "$TRIP_DB_PASSWORD" in *[!a-zA-Z0-9_!@%+=-]*) echo 'Unsupported characters in TRIP_DB_PASSWORD' >&2; exit 1;; esac
export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
exec /opt/mssql-tools18/bin/sqlcmd -S sqlserver -U sa -C -b -v TripPassword="$TRIP_DB_PASSWORD" -i /init.sql
