# Trade Performance Tests

This package is the Trade vertical extension point for the shared NGB performance framework.

The first pass includes a smoke scaffold that validates authentication, health, and shared metadata/report surfaces. Future work should add Trade-specific business-day scenarios using Trade document types, catalogs, reports, and fixture strategy.

```bash
cd performance-tests
cp ngb-trade-perf/.env.example ngb-trade-perf/.env.local
./scripts/run-k6.sh --env-file ngb-trade-perf/.env.local --test ngb-trade-perf/src/tests/smoke.ts
```
