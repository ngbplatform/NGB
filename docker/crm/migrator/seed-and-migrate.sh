#!/usr/bin/env sh
set -eu

connection_string="Host=${CRM_DB_HOST};Port=${CRM_DB_PORT};Database=${CRM_DB_NAME};Username=${CRM_DB_USER};Password=${CRM_DB_PASSWORD}"

dotnet /app/NGB.CRM.Migrator.dll \
  --connection "${connection_string}" \
  --modules crm \
  --repair

dotnet /app/NGB.CRM.Migrator.dll \
  seed-defaults \
  --connection "${connection_string}"

if [ "${CRM_DEMO_SEED_ENABLED:-true}" = "true" ]; then
  dotnet /app/NGB.CRM.Migrator.dll \
    seed-demo \
    --connection "${connection_string}"
fi
