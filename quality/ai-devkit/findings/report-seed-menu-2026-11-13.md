# Missing base menu registration in the report starting fixture

Candidate `release-candidate-local-20261113-090206` passed the Kit application
profile in 377.6 seconds. The Codex root catalog, document and permission scenarios
completed before report acceptance stopped on three `MainMenuHttpTests` failures.
The report's four independent HTTP tests, 111 unit tests and eight newly authored
integration tests passed; its selected regression batch had 50 passed and three
failed cases.

The report starting overlay replaced `EquipmentRuntime.cs` with a copy missing
`IMainMenuContributor, EquipmentMainMenuContributor`. That omission existed before
model inference: the original starting inventory matches the faulty seed bytes.
Consequently, the merge tests saw only their extra lowercase group contribution,
and granting catalog-view permission exposed no equipment navigation. The SDK menu
implementation, report calculation and permission rules were not the cause.

## Correction

The maintained report seed now preserves the base menu registration alongside its
category registrations. Its manifest hash was updated. The new contract test
checks all three source overrides shared with the current example: Definitions,
Runtime composition and PostgreSQL composition. Removing only the declared category
additions must reproduce the base file exactly. An unreviewed shared override,
missing prerequisite or unrelated removal/change fails this check.

The contract test belongs to the existing early tooling suite. It reproduced the
missing registration before the fix without containers or model calls, then passed
afterwards. This guards the whole shared baseline rather than only the menu line.
Historical candidate/evaluation evidence is unchanged. No SDK, migration, test
assertion, acceptance requirement, production application or packaged Kit source
was changed.

## Verification

[Diagnostic artifacts](../../../artifacts/ai-devkit/diagnostics/report-seed-menu-20261113)
retain the original source identities, isolated copies, scripts, logs and TRX files.
An exact copy of the generated source reproduced the same three failures and one
pass in the four menu cases, in 28.7 seconds including compilation.

The corrected diagnostic copy restores only the one missing registration. Generated
report code and all test sources are byte-identical to the original. Its manual
review selection preserves every originally selected integration class. This
diagnostic repair is not a new model inference and cannot overwrite or replace
the original failed model receipt.

The complete report-scenario acceptance passed in 131.9 seconds: four independent
HTTP cases, all 111 unit cases, eight new integration cases and all 53 selected
regression cases. All four menu cases passed, including the three that originally
failed. Source inventory checks confirmed that execution did not modify the
reviewed diagnostic application. Twenty targeted fixture/oracle/review tests passed,
and preparation confirmed zero changes to the 317 packaged guidance files.

FULL Certification, full platform Backend/Frontend/coverage suites and new
Codex/Claude invocations were not run.
