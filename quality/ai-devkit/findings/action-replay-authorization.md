# Document action replay authorization

Status: corrected in the SDK before 3.4.0 publication after explicit authorization
to fix the replay path. Earlier sealed candidates retain the reproduced failures.
Verification for the correction is recorded under
`artifacts/ai-devkit/action-replay-fix-3.4.0/` and the replacement candidate's
certification reports. These tests do not establish practical exploitability.

## Reproduction

The external equipment/inspection fixture uses only public NuGet packages, the
standard document controller/dispatcher, a disposable PostgreSQL database and
real Keycloak tokens. No platform user record or permission grant exists for the
limited actor.

1. An active `ngb-admin` creates and posts a complete inspection using an explicit
   idempotency key and the current expected version.
2. A different authenticated actor cannot read that document: HTTP 403.
3. The same limited actor submits the completed action's type, ID, action code,
   original expected version and idempotency key: HTTP 200 instead of 403.

The caller must know the complete request and key; this reproduction does not
claim anonymous access or that the keys are discoverable. The returned stored
action result includes its document payload. An idempotency key must not itself
grant permission to read that payload.

Practical impact has not been established. The first test deliberately gives the
second actor the complete original request and key; it does not demonstrate a
way to obtain them. A separate test confirms that a subsequently deactivated
actor can replay its own completed request while document GET and a new action
both return 403. That actor receives the original result it already possessed,
not newly fetched document data. Neither reproduction executes the action twice
or demonstrates access to arbitrary documents.

Regression:
`ai-devkit/examples/equipment-inspection/EquipmentInspection.IntegrationTests/InspectionLifecycleTests.cs`,
`Unauthorized_actor_cannot_replay_an_administrator_action_result`.

Initial evidence: `artifacts/ai-devkit/lifecycle-probe-3.4.0/lifecycle-probe.trx`.
This development probe used the SDK bytes from `candidate-lifecycle-3.4.0` and a
corrected example import; it is not a successful release certificate.

The sealed-package run in
`artifacts/ai-devkit/examples-lifecycle-3.4.0-3/examples.json` preserves the first
failed regression alongside 25 passing tests and complete example source
coverage. The separate deactivation probe is retained at
`artifacts/ai-devkit/deactivated-replay-3.4.0/deactivated-replay.trx`.

## Original cause and correction

The original `DocumentActionDispatcher.ExecuteCoreAsync` called `TryBeginAsync`
and returned a completed stored result before refreshing the caller's permission
snapshot or running `DocumentActionEvaluator`. `AddNgbRuntimeAuthorization` does
not decorate this dispatcher. Normal first execution checks permissions later
inside the transaction, so ordinary denied-action tests did not detect that path.

The corrected completed-result branch:

- Authorizes access to the completed result using the current active actor,
  current document access and the registered action's authorization contract,
  including custom authorization evaluators and facts where applicable.
- Keeps authorization separate from action availability. An authorized repeat of
  Post must still return its original result after the document became Posted;
  it must not rerun the handler or fail because the original version is stale.
- Checks access to any created document before returning stored results, including
  custom-handler results. It preserves transaction behavior, conflict semantics
  and existing permission keys, and uses bounded reads without taking the source
  document write lock.

`DocumentActionReplayAuthorizationTests` covers refreshed/revoked permissions,
inactive and unauthenticated actors, current custom facts, created-document access,
missing documents, type mismatches and successful replay after status/version
changes. The external HTTP suite keeps both original denial assertions, verifies
authorized retry after a denied attempt and simultaneous retries with the same key.
The custom-action example now expects one current authorization fact load on
replay while retaining its unchanged document and single audit-entry assertions.

Do not bypass the failure with a different lifecycle, controller-only special
case, test exclusion or weakened expected result. Historical failed reports must
not be rewritten as passes.
