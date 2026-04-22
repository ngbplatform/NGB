#!/usr/bin/env sh
set -eu

connection_string="Host=${TRADE_DB_HOST};Port=${TRADE_DB_PORT};Database=${TRADE_DB_NAME};Username=${TRADE_DB_USER};Password=${TRADE_DB_PASSWORD}"

dotnet /app/NGB.Trade.Migrator.dll \
  --connection "${connection_string}" \
  --modules trade \
  --repair

dotnet /app/NGB.Trade.Migrator.dll \
  seed-defaults \
  --connection "${connection_string}"

if [ "${TRADE_DEMO_SEED_ENABLED:-true}" = "true" ]; then
  set -- seed-demo --connection "${connection_string}" --skip-if-activity-exists true

  if [ -n "${TRADE_DEMO_SEED:-}" ]; then
    set -- "$@" --seed "${TRADE_DEMO_SEED}"
  fi

  if [ -n "${TRADE_DEMO_SEED_FROM:-}" ]; then
    set -- "$@" --from "${TRADE_DEMO_SEED_FROM}"
  fi

  if [ -n "${TRADE_DEMO_SEED_TO:-}" ]; then
    set -- "$@" --to "${TRADE_DEMO_SEED_TO}"
  fi

  if [ -n "${TRADE_DEMO_CLOSE_PERIODS:-}" ]; then
    set -- "$@" --close-periods "${TRADE_DEMO_CLOSE_PERIODS}"
  fi

  dotnet /app/NGB.Trade.Migrator.dll "$@"
fi
