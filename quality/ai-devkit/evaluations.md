# Real-agent evaluation runners

These runners execute the frozen tasks in `agent-evaluations.json` against an
external application and sealed package candidate. They use the installed client
and its existing subscription login. A completed process is not a passing result:
review the exact produced tree, run the independent oracle and record its outcome.
The complete client/task/root-and-subdirectory matrix remains a release requirement.

Use the pinned Node 24.19.0 and .NET SDK 10.0.401 from the certification toolchain.
The client versions, model selection, time/usage ceilings and tool restrictions
are declared in each client/family profile before inference. Do not automatically
retry a failed task, repair its output or use oracle feedback as a hint. Keep the
original redacted transcript, partial journals, failure and produced files.

Client output is sanitized before journal or transcript writes. The streaming
runners buffer complete UTF-8 lines so credentials split across process chunks
cannot reach disk. They redact credential-bearing HTTP/SOCKS URLs, separate proxy
user/password fields, authorization values and labeled secrets while preserving
JSON event structure and numeric usage/cost fields. A cancelled, incomplete URL
fragment is withheld. Journals become visible after each complete line; a final
incomplete record may remain invalid JSON and does not establish agent success.
Codex final-answer artifacts are derived from that sanitized event stream; the
client is not given a separate raw `--output-last-message` destination.
Recorded environment values use the same redaction before configuration hashing;
the running child retains its required transport credentials without exposing them
in the saved configuration.

Every attempt retains the redactor source and hash alongside its runner and profile.
The verifier checks those identities and refuses a transcript that still needs
sanitization before copying it into certification evidence. Earlier private logs
remain historical diagnostics; a sanitized diagnostic copy is not a replacement
evaluation or a passing retry. Do not publish legacy raw logs containing credentials.

## Customized 3.3 to 3.4 upgrade

Prepare a new directory outside the repository for every attempt:

```sh
node quality/ai-devkit/prepare-upgrade-evaluation.mjs <candidate> <new-fixture>
node quality/ai-devkit/evaluate-codex-upgrade.mjs customized-kit-upgrade root <candidate> <new-fixture> <new-output> <codex-binary>
```

For Claude use `evaluate-claude-upgrade.mjs` and its executable. Use `subdirectory`
instead of `root` for the second working-directory case. The fixture preparer
verifies the immutable 3.3 source, supplies the exact target CLI separately and
proves that the declared ownership collision leaves preview read-only. The client
can edit the application and private scratch; target packages and CLI stay read-only.
The task permits managed instructions/adapters to change through the upgrade.
Codex receives explicit write grants for the application skill directories; a broad
application write grant alone retains native protection on `.agents`. Preflight
creates and removes a temporary directory there before inference.

`--preflight-only` runs the environment checks without a model request. Use a
separate fresh fixture/output afterward; do not resume or reuse the preflight's
scratch as an attempt. The preflight checks file access boundaries, target CLI
immutability, a cold public NuGet restore, npm metadata, rejected undeclared hosts
and rejected direct egress. It does not run the entire upgrade or prove task success.

The native network profile permits `api.nuget.org` and `registry.npmjs.org` only.
Child tools retain the sandbox proxy transport, including ephemeral authentication
on an exact loopback host. Credentials are redacted from diagnostics. macOS .NET
requires its shared-memory/lock directories for first-run initialization; Claude's
profile also allows the narrow `com.apple.trustd.agent` lookup for certificate
verification. These exceptions are recorded in the profiles. Docker and the SDK
checkout remain inaccessible. Claude's standalone sandbox-runtime preflight does
not claim binary equivalence with the installed client's own native sandbox.

After reviewing all changes, run the independent oracle against that exact hash:

```sh
node quality/ai-devkit/run-upgrade-oracle.mjs <candidate> <application> <trusted-fixture> <new-oracle-output> <reviewed-tree-sha256>
```

The oracle checks preservation of application/custom content, exact managed
payloads and dependencies, installed Kit integrity and clean locked backend/helper
and frontend builds. Its receipt is distinct from agent-process evidence. A
preflight, deterministic control or earlier candidate cannot substitute for a
successful real-agent attempt with matching inputs.

## Observed Claude native-sandbox upgrade limitation

The real Claude Code 2.1.308 subdirectory attempt reached successful staged builds
but its native Bash sandbox denied `.claude/skills` at commit. All 65 original files
were preserved by rollback; independent acceptance rejects the unupgraded tree.
A standalone sandbox-runtime preflight did not predict this additional client
protection. Do not treat that preflight as proof that native upgrade commit works.

[Claude's protected-path documentation](https://code.claude.com/docs/en/sandboxing#protected-paths)
states that `allowWrite` and `Edit` allow rules do not override this protection.
The historical v1 profile is incompatible with autonomous skill installation and
is not a release pass. Retain its failed attempt. The v2 upgrade profile provides
one fixed installer command for the explicitly authorized managed-file update.
Ordinary Bash commands retain native filesystem/network isolation.

## Bounded Claude upgrade installer

The runner generates an immutable, no-argument launcher outside the writable
application and scratch. Only that exact command is listed in
[`sandbox.excludedCommands`](https://code.claude.com/docs/en/settings-reference#sandbox-excludedcommands).
It starts the pinned sandbox runtime with fixed filesystem/network rules, then
invokes the supplied CLI's `upgrade --apply` for one application, candidate and
resolution-file path. It does not accept a shell command, extra flags, alternate
paths or inherited interpreter settings. Its clean environment retains only
explicit toolchain values; the sandbox creates its own restricted network proxy.
Both `TMPDIR` and `CLAUDE_CODE_TMPDIR` select the same private scratch directory.

The installer may write application skill adapters. Git, client configuration,
candidate packages, target CLI, unrelated user files and undeclared network hosts
remain inaccessible as specified by its policy. Empty `.claude` is created during
trusted preparation so Seatbelt can protect the missing settings files beneath
it without denying creation of that parent. No guidance or settings are seeded.
Preflight creates/removes probe files in both skill directories, rejects client
settings writes, checks immutable input access, and exercises restricted restores.

The agent still previews the upgrade and authors its hash-bound resolution.
It invokes the fixed installer without arguments or redirection, then verifies
the result. Independent acceptance checks the exact generated tree afterward.
Saved configuration includes the launcher, its generator hash, sandbox settings
and clean environment. The profile pins the generator source; certification
checks that binding. Tests exercise literal shell quoting, rejection of extra
arguments and removal of inherited Node options, proxies and credentials.

Preflight and a deterministic installer control are environment evidence only.
They do not establish a real Claude task pass or replace the complete matrix.
