#!/usr/bin/env sh
set -eu

connection_string="Host=${PM_DB_HOST};Port=${PM_DB_PORT};Database=${PM_DB_NAME};Username=${PM_DB_USER};Password=${PM_DB_PASSWORD}"

dotnet /app/NGB.PropertyManagement.Migrator.dll \
  --connection "${connection_string}" \
  --modules pm \
  --repair

dotnet /app/NGB.PropertyManagement.Migrator.dll \
  seed-defaults \
  --connection "${connection_string}"

if [ "${PM_DEMO_SEED_ENABLED:-true}" = "true" ]; then
  set -- seed-demo --connection "${connection_string}" --skip-if-dataset-exists true

  if [ -n "${PM_DEMO_DATASET:-}" ]; then
    set -- "$@" --dataset "${PM_DEMO_DATASET}"
  fi

  if [ -n "${PM_DEMO_SEED:-}" ]; then
    set -- "$@" --seed "${PM_DEMO_SEED}"
  fi

  if [ -n "${PM_DEMO_SEED_FROM:-}" ]; then
    set -- "$@" --from "${PM_DEMO_SEED_FROM}"
  fi

  if [ -n "${PM_DEMO_SEED_TO:-}" ]; then
    set -- "$@" --to "${PM_DEMO_SEED_TO}"
  fi

  if [ -n "${PM_DEMO_BUILDINGS:-}" ]; then
    set -- "$@" --buildings "${PM_DEMO_BUILDINGS}"
  fi

  if [ -n "${PM_DEMO_UNITS_MIN:-}" ]; then
    set -- "$@" --units-min "${PM_DEMO_UNITS_MIN}"
  fi

  if [ -n "${PM_DEMO_UNITS_MAX:-}" ]; then
    set -- "$@" --units-max "${PM_DEMO_UNITS_MAX}"
  fi

  if [ -n "${PM_DEMO_TENANTS:-}" ]; then
    set -- "$@" --tenants "${PM_DEMO_TENANTS}"
  fi

  if [ -n "${PM_DEMO_VENDORS:-}" ]; then
    set -- "$@" --vendors "${PM_DEMO_VENDORS}"
  fi

  if [ -n "${PM_DEMO_OCCUPANCY_RATE:-}" ]; then
    set -- "$@" --occupancy-rate "${PM_DEMO_OCCUPANCY_RATE}"
  fi

  dotnet /app/NGB.PropertyManagement.Migrator.dll "$@"
fi
