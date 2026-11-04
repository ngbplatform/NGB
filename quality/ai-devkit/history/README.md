# Superseded evaluation inputs

These frozen inputs explain earlier development reports. They are historical
evidence, not the current SDK guidance or acceptance contract.

- `skill-discovery-oracle-v1.mjs` accepted only instruction directory paths.
  Version 2 also accepted the canonical instruction file paths.
- `agent-evaluations-v1.json` and both earlier discovery oracles incorrectly
  named the missing-handler outcome as a zero-effect limitation. On 2026-10-26
  source inspection confirmed the distinction: standard Post requires a handler,
  but that handler can intentionally emit no records. The current matrix/oracle
  separately require successful empty-effect posting and missing-handler refusal.
- Claude discovery profiles v1/v2 enabled restricted mode, which suppressed
  native project skill discovery. The current profile permits project discovery
  with explicit read-only tool and filesystem restrictions.
- `skill-discovery-oracle-v3.mjs` distinguishes empty effects from missing
  handlers but leaves the status field's string format unspecified. The current
  response schema explicitly requests an enum member without prose; expected
  outcomes and the acceptance checks are unchanged.

Keep prior raw transcripts and frozen inputs unchanged. Earlier passes do not
certify the corrected matrix or an updated Kit payload; execute fresh attempts.

Codex discovery profile v2 records the previously installed 0.165.0-alpha.1.2
client. Version 3 pins the observed 0.166.0-alpha.5.1 binary; earlier discovery
results do not establish compatibility with this updated client.

Codex development profile v1 retains the first real catalog attempt: its produced
feature passed all six independent cases, but the attempt exceeded the total-token
ceiling and one agent-authored HTTP test failed while creating duplicate role codes.
Profile v2 separately limits cumulative and uncached token usage, and the fixture
preparation now requires valid CLI configuration/locks before inference. The failed
v1 attempt is not retried, repaired or reclassified by these changes.

Claude upgrade profile v1 retains the native-sandbox attempt that rolled back when
`.claude/skills` could not be installed. Version 2 keeps native isolation for normal
commands and provides one immutable installer with its own restricted OS sandbox.
The earlier failed attempt does not certify that revised execution profile.
