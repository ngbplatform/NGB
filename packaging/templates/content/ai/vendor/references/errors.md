# Application errors and HTTP boundaries

Target: NGB 3.4.0. Use the public
[Tools error contracts](contracts/NGB.Platform.Tools.json) and
[ASP.NET Core hosting contracts](contracts/NGB.Platform.Hosting.AspNetCore.json).

Runtime errors stay independent of HTTP. `INgbError` supplies a stable
`ErrorCode`, `Kind` and structured `Context`. Derive an application-specific
exception from the appropriate `NgbValidationException`, `NgbNotFoundException`,
`NgbConflictException` or `NgbForbiddenException` base when the workflow needs its
own stable code. The predefined required/invalid/out-of-range argument exceptions
already carry field identity for the standard validation response. Do not use a
configuration or invariant failure to represent normal incomplete Draft input.

`NgbExceptionPolicy.Apply` preserves cancellation, existing structured NGB errors
and provider `DbException` instances. It wraps a timeout with `ngb.infra.timeout`
and other unexpected failures with `ngb.unexpected`, preserving the original as
`InnerException`. These wrappers omit the original message from their public
message and context. Additional context is copied as supplied: include only
deliberately public, bounded values. `Exception.ToString()`, `InnerException`,
raw provider diagnostics and arbitrary context are not safe HTTP response bodies.
The policy is not a general-purpose secret scrubber.

Keep the template's `AddGlobalErrorHandling` registration and exception-handler
middleware. The standard HTTP response uses `application/problem+json`, a stable
`error.code`/`error.kind`, the request `instance` and a `traceId`. Field validation
includes `error.errors` and normalized `error.issues`. Consumers should handle
codes and fields rather than parsing the English detail message.

| Error kind | Default HTTP status |
| --- | --- |
| Validation | 400 |
| NotFound | 404 |
| Conflict | 409 |
| Forbidden | 403 |
| Configuration, Infrastructure, unexpected fallback | 500 |
| Explicit timeout | 504 |

Non-5xx structured errors expose their chosen message. Server failures use a
generic detail; structured context remains the application's responsibility.
Raising a forbidden error is only a transport signal, not an implementation of
authorization. Existing current-user application services still decide access.

Provider-specific translation belongs at the host composition boundary through
`INgbExceptionHttpMapper`. Return a supported status, stable code/kind and safe
context for a recognized provider failure; return null for unrelated exceptions.
Keep provider dependencies out of Runtime. Do not map an unknown storage failure
to a successful business result or replace the platform's authorization checks.

The independent `ErrorBoundaryContractTests` exercise the real Production HTTP
middleware on a loopback server. They verify argument and application error
families, field details, tracing, timeout/fallback behavior and a synthetic
provider mapper returning 503. Separate policy checks preserve cancellation and
provider exception identity while retaining private messages only in the inner
exception. The fixture does not expose a business endpoint or prove arbitrary
application context is sanitized; real provider/network failures have their own
integration scenarios.
