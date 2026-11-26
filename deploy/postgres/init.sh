#!/bin/sh
set -eu
export PGHOST=postgres PGDATABASE=postgres PGUSER=swiftride_admin
export PGPASSWORD="$POSTGRES_ADMIN_PASSWORD"
psql -X -v ON_ERROR_STOP=1 <<'SQL'
\getenv trip_password TRIP_DB_PASSWORD
\getenv pay_password PAY_DB_PASSWORD
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', name, password)
FROM (VALUES ('trip', :'trip_password'), ('payment_app', :'pay_password')) AS accounts(name, password)
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = name) \gexec
SELECT format('CREATE DATABASE %I OWNER %I', name, owner)
FROM (VALUES ('TripDB', 'trip'), ('PayDB', 'payment_app')) AS databases(name, owner)
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = name) \gexec
REVOKE ALL ON DATABASE "TripDB", "PayDB" FROM PUBLIC;
REVOKE CONNECT ON DATABASE postgres, template1 FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE "TripDB" TO trip;
GRANT CONNECT, TEMPORARY ON DATABASE "PayDB" TO payment_app;
REVOKE ALL ON DATABASE "TripDB" FROM payment_app;
REVOKE ALL ON DATABASE "PayDB" FROM trip;
ALTER DATABASE "PayDB" OWNER TO payment_app;
ALTER DATABASE "TripDB" OWNER TO trip;
ALTER ROLE trip NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
ALTER ROLE payment_app NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
\connect TripDB
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO trip;
\connect PayDB
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO payment_app;
SQL
