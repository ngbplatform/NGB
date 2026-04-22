#!/bin/sh
set -eu

psql_admin() {
  PGPASSWORD="$POSTGRES_ADMIN_PASSWORD" \
    psql \
      -v ON_ERROR_STOP=1 \
      --host "$POSTGRES_HOST" \
      --port "$POSTGRES_PORT" \
      --username "$POSTGRES_ADMIN_USER" \
      --dbname postgres \
      "$@"
}

ensure_role_credentials() {
  role_name="$1"
  role_password="$2"

  psql_admin --set=role_name="$role_name" --set=role_password="$role_password" <<'SQL'
SELECT format(
  'CREATE ROLE %I LOGIN PASSWORD %L',
  :'role_name',
  :'role_password'
)
WHERE NOT EXISTS (
  SELECT 1
  FROM pg_roles
  WHERE rolname = :'role_name'
) \gexec

SELECT format(
  'ALTER ROLE %I WITH LOGIN PASSWORD %L',
  :'role_name',
  :'role_password'
) \gexec
SQL
}

create_database_if_missing() {
  db_name="$1"
  db_owner="$2"

  psql_admin --set=db_name="$db_name" --set=db_owner="$db_owner" <<'SQL'
SELECT format(
  'CREATE DATABASE %I OWNER %I',
  :'db_name',
  :'db_owner'
)
WHERE NOT EXISTS (
  SELECT 1
  FROM pg_database
  WHERE datname = :'db_name'
) \gexec
SQL

  psql_admin --set=db_name="$db_name" --set=db_owner="$db_owner" <<'SQL'
SELECT format(
  'GRANT ALL PRIVILEGES ON DATABASE %I TO %I',
  :'db_name',
  :'db_owner'
) \gexec
SQL
}

ensure_role_credentials "$KEYCLOAK_DB_USER" "$KEYCLOAK_DB_PASSWORD"
create_database_if_missing "$KEYCLOAK_DB_NAME" "$KEYCLOAK_DB_USER"

ensure_role_credentials "$APP_DB_USER" "$APP_DB_PASSWORD"
create_database_if_missing "$APP_DB_NAME" "$APP_DB_USER"
