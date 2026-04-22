#!/usr/bin/env sh
set -eu

connection_string="Host=${AB_DB_HOST};Port=${AB_DB_PORT};Database=${AB_DB_NAME};Username=${AB_DB_USER};Password=${AB_DB_PASSWORD}"

dotnet /app/NGB.AgencyBilling.Migrator.dll \
  --connection "${connection_string}" \
  --modules agency-billing \
  --repair

dotnet /app/NGB.AgencyBilling.Migrator.dll \
  seed-defaults \
  --connection "${connection_string}"

if [ "${AB_DEMO_SEED_ENABLED:-true}" = "true" ]; then
  set -- seed-demo --connection "${connection_string}" --skip-if-activity-exists true

  if [ -n "${AB_DEMO_SEED:-}" ]; then
    set -- "$@" --seed "${AB_DEMO_SEED}"
  fi

  if [ -n "${AB_DEMO_SEED_FROM:-}" ]; then
    set -- "$@" --from "${AB_DEMO_SEED_FROM}"
  fi

  if [ -n "${AB_DEMO_SEED_TO:-}" ]; then
    set -- "$@" --to "${AB_DEMO_SEED_TO}"
  fi

  if [ -n "${AB_DEMO_CLIENTS:-}" ]; then
    set -- "$@" --clients "${AB_DEMO_CLIENTS}"
  fi

  if [ -n "${AB_DEMO_TEAM_MEMBERS:-}" ]; then
    set -- "$@" --team-members "${AB_DEMO_TEAM_MEMBERS}"
  fi

  if [ -n "${AB_DEMO_PROJECTS:-}" ]; then
    set -- "$@" --projects "${AB_DEMO_PROJECTS}"
  fi

  if [ -n "${AB_DEMO_TIMESHEETS:-}" ]; then
    set -- "$@" --timesheets "${AB_DEMO_TIMESHEETS}"
  fi

  if [ -n "${AB_DEMO_SALES_INVOICES:-}" ]; then
    set -- "$@" --sales-invoices "${AB_DEMO_SALES_INVOICES}"
  fi

  if [ -n "${AB_DEMO_CUSTOMER_PAYMENTS:-}" ]; then
    set -- "$@" --customer-payments "${AB_DEMO_CUSTOMER_PAYMENTS}"
  fi

  dotnet /app/NGB.AgencyBilling.Migrator.dll "$@"
fi
