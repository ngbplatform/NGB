# Critical mutation-equivalent review

The implementation specification permits targeted mutation testing **or an equivalent strong
negative-test review**. This release uses the latter because the repository does not carry a
mutation-runner dependency. The executable gate is:

```bash
quality/mutation/run-critical-mutation-review.sh
```

The gate exercises both positive and deliberately inverted boundary cases. The following mutation
classes have no surviving critical case:

| Critical logic | Mutations challenged | Evidence |
|---|---|---|
| Action registry validation | remove duplicate/type/handler checks; invert command/view rules; change case-insensitive comparison | `DocumentActionPlatformTests`, `DocumentActionDefinitionAndCoreCoverageTests`, `DocumentActionEvaluatorCoverageTests` |
| Permission filtering | allow unauthenticated/inactive identities; remove one derivation permission; expose a denied Work Center source | evaluator permission matrix, `WorkCenterServicesTests`, PM HTTP source-IDOR assertions |
| Availability composition | ignore standard/custom reasons; replace AND/OR; stop deterministic ordering | evaluator standard lifecycle matrix and custom reason ordering, PM/CRM policy tests |
| Preference resolution | invert default/current/mandatory precedence; allow mandatory or locked definitions to be disabled | `WorkCenterServicesTests.Query_service_resolves_and_updates_preferences_with_mandatory_guards` |
| Task deduplication | remove dedup key uniqueness/reuse; complete/cancel the wrong key | runtime service tests and `PmWorkCenter_HttpAndPersistence_P0Tests` |
| Task auto-completion | remove complete/cancel transitions for terminal events | `PropertyManagementWorkCenterPolicyTests`, `CrmWorkCenterPolicyTests` |
| Cursor encoding/decoding | accept malformed payloads; change keyset ordering; omit cursor boundary | Work Center malformed-cursor theory and two-page PM HTTP test |
| Outbox retry decisions | change maximum-attempt comparison; invert dead-letter choice; remove backoff; swallow cancellation | `OutboxProcessorTests` retry/dead-letter/cancellation matrix and PostgreSQL retry-history test |

The review also requires the 100% feature line/branch gate. This prevents an unreviewed critical
branch from being added without executable evidence. There are no documented critical survivors.
