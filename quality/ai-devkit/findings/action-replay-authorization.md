# Document action replay authorization

Status: a permission-check inconsistency was reproduced against the sealed
3.4.0 SDK. The user explicitly declined an SDK change and requested continued
Kit work. Preserve platform behavior and report the failing assertions accurately.
Do not present these tests as evidence of an easily exploitable data leak or
expand the Kit implementation into an SDK security investigation.

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

## Cause and proposed bounded correction

`DocumentActionDispatcher.ExecuteCoreAsync` calls `TryBeginAsync` and returns a
completed stored result before refreshing the caller's permission snapshot or
running `DocumentActionEvaluator`. `AddNgbRuntimeAuthorization` does not decorate
this dispatcher. Normal first execution checks permissions later inside the
transaction, so ordinary denied-action tests do not detect the replay path.

Proposed SDK change, outside the authorized implementation scope:

- Authorize access to the completed result using the current active actor,
  current document access and the registered action's authorization contract,
  including custom authorization evaluators and facts where applicable.
- Keep authorization separate from action availability. An authorized repeat of
  Post must still return its original result after the document became Posted;
  it must not rerun the handler or fail because the original version is stale.
- Check access to any created document before returning derivation results.
  Preserve transaction behavior, conflict semantics and existing permission keys.
- Add regression cases for a denied actor, a subsequently deactivated/revoked
  actor, custom authorization, derivation data, successful authorized replay and
  concurrent requests. Verify the existing action/performance suites before
  repackaging and re-running independent acceptance.

Do not bypass the failure with a different lifecycle, controller-only special
case, test exclusion or weakened expected result. No SDK code has been changed
as part of this finding.
