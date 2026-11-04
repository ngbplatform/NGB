---
name: ngb-background-jobs
description: Compose an NGB Hangfire worker, register application jobs and schedules, finalize register projections, and verify failure, cancellation and restart behavior.
---

Read [background jobs]({{references}}/background-jobs.md) before composing a
worker or adding a job. Reuse an existing platform job when it performs the
required operation; OR projection finalization already has a job.

Keep registrations shared with the API and put orchestration in the runtime
module. A custom job implements `IPlatformBackgroundJob`; register its stable ID
through `IBackgroundJobCatalogContributor` and supply an explicit schedule.
Catalog membership does not schedule or authorize work by itself.

Define the transaction, batch limit and replay key before implementing effects.
The supplied runner prevents overlap per JobId and disables automatic Hangfire
retries. Decide how an operator or a later scheduled run should resume partial
work; do not describe this as exactly-once delivery.

Verify real storage, execution through the worker, repeated delivery, failure
visibility, cancellation and restart. Check the desired-versus-actual schedule
report. Keep the dashboard protected and external notification destinations
application-owned. Run the application's build and integration test profiles.
