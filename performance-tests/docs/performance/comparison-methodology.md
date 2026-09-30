# Comparison methodology

## Establish equivalent work

A release number or matching test name is insufficient. Preserve the k6 scenario
configuration (executors, stages, rates, VU allocation, duration, pauses, thresholds),
workload settings and fixture identities. Check whether writes, fresh posting, replay,
period close, account-scoped reports and long-period exports were enabled.

Record the application image, Git revision and dirty state, test source hash, k6 version,
container CPU/memory limits, database configuration and the generator's resources.
Record the restored database snapshot identity and restoration procedure. Reusing the
same database name is not proof of the same data: write workloads accumulate documents.
Keep warm-up/cache policy and competing activity consistent; record restarts, disk
cleanup, checkpoint resets and host sleep. The runner records available identities but
does not restore the database or establish a controlled A/B automatically.

When evidence is incomplete, label the comparison **observational**, enumerate the
confounders and avoid attributing the entire speedup to one code change. Repeated matched
runs are needed for repeatability claims; one run does not yield a confidence interval.
Never silently replace a baseline with the latest file found in a directory.

## Evaluate validity before latency

- Require completed execution, a usable summary and matching time-series totals.
- Check HTTP/business errors, failed checks, dropped iterations, auth failures and HTTP 429.
- A threshold PASS may allow nonzero errors. Report both the threshold result and counts.
- Missing counters and unused zero-valued trend slices are not measured zero latency/errors.
- Exclude interrupted runs, sleep-corrupted timelines, wrong fixtures and closed-period failures.
- Inspect generator CPU/RSS, VU allocation and metric cardinality before blaming API capacity.
- Compare database counter increments only when reset identities and monotonicity are valid.

## Interpret performance

Report operation-specific p50/p95/p99 and maxima alongside aggregate HTTP values. A small
fraction of slow postings can be hidden by millions of fast reads. Inspect time windows
for late-run degradation, error bursts, lock queues and cardinality growth. Do not average
percentiles across tests or take the average of minute p95 values as an overall p95.

Arrival-rate profiles measure whether the system sustains the configured demand; their
RPS is not a maximum throughput estimate. Capacity profiles vary concurrent users. Compare
stable hold windows, not only the aggregate over ramps and holds. Validate observed VUs,
errors, operation latency and resource behavior at each stage. If every stage succeeds,
report “sustained through the highest tested stage”; no breakpoint was established.
A finite run does not prove unlimited soak stability or the correctness of every consumer.

## Preserve a reviewed conclusion

Save an English report under `artifacts/reports/` containing the decision, configuration,
key measurements, source run identifiers, limitations and unresolved questions. Keep
run-specific reports, evidence indexes and raw logs/time series outside Git. Archive the
report and its supporting evidence externally when long-term retention is needed; include
source hashes and the archive location. Document lasting implementation contracts near
the owning component and preserve executable correctness checks as regression tests.
Never commit credentials, tokens, complete Docker environments or business row dumps.

## Primary references

The distinction between aggregate summaries and timestamped metrics follows
[Grafana k6 result output](https://grafana.com/docs/k6/latest/get-started/results-output/).
The saved stream uses [k6 JSON output](https://grafana.com/docs/k6/latest/results-output/real-time/json/),
including gzip output. The database connection configuration uses the
[node-postgres environment-variable interface](https://node-postgres.com/features/connecting).
These references were reviewed on 2026-09-26; project workload contracts remain authoritative.
