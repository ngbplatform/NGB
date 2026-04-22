#!/usr/bin/env bash
set -euo pipefail

realm_file="${KEYCLOAK_IMPORT_FILE:-/opt/keycloak/data/import/ngb-realm.json}"
bootstrap_log="$(mktemp)"
bootstrap_username="${KC_BOOTSTRAP_ADMIN_USERNAME:-temp-admin}"

# Import creates the master realm before the normal server start path runs,
# so bootstrap the admin user here to keep the admin console accessible.
if ! /opt/keycloak/bin/kc.sh bootstrap-admin user \
  --optimized \
  --username:env KC_BOOTSTRAP_ADMIN_USERNAME \
  --password:env KC_BOOTSTRAP_ADMIN_PASSWORD \
  --no-prompt >"$bootstrap_log" 2>&1; then
  cat "$bootstrap_log"
  rm -f "$bootstrap_log"
  exit 1
fi

if grep -q "Created temporary admin user" "$bootstrap_log"; then
  printf 'Keycloak bootstrap admin user "%s" created.\n' "$bootstrap_username"
elif grep -q "user with username exists" "$bootstrap_log"; then
  printf 'Keycloak bootstrap admin user "%s" already exists.\n' "$bootstrap_username"
else
  cat "$bootstrap_log"
fi

rm -f "$bootstrap_log"

/opt/keycloak/bootstrap/render-realm.sh "$realm_file"

# Keep the local demo realm declarative: env/seed changes should be applied
# on rebuilds even when the Postgres data volume is preserved.
exec /opt/keycloak/bin/kc.sh import --file "$realm_file" --override true
