# Agency Billing Performance Tests

This package is the Agency Billing vertical extension point for the shared NGB performance framework.

The first pass includes a smoke scaffold that validates authentication, health, and shared metadata/report surfaces. Future work should add Agency Billing-specific business-day scenarios using AB document types, catalogs, reports, and fixture strategy.

```bash
cd performance-tests
cp ngb-agency-billing-perf/.env.example ngb-agency-billing-perf/.env.local
./scripts/run-k6.sh --env-file ngb-agency-billing-perf/.env.local --test ngb-agency-billing-perf/src/tests/smoke.ts
```
