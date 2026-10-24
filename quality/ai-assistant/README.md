# MCP and AI Assistant verification

The assistant and MCP adapter reuse authorized application reads. These checks
must not add domain filters, calculations, reports or write operations to make an
assistant scenario pass. An unsupported selection must remain an explicit limit.

## Automated checks

The following suites cover the MCP adapter and AI Assistant:

- `NGB.Mcp.Tests`: schemas, dispatch, read budgets and adapter boundaries.
- `NGB.Mcp.AspNetCore.IntegrationTests`: real HTTP and MCP SDK transport,
  authorization, protocol negotiation and cancellation.
- `NGB.AiAssistant.Tests`: bounded conversation execution, context retention,
  retries, exact display values and numeric evidence checks.
- `NGB.AiAssistant.Providers.Tests`: provider configuration and conversation contracts.
- `NGB.AiAssistant.AspNetCore.IntegrationTests` excluding `NativeOllama`: HTTP,
  current-user MCP access, sources and result projections with controlled models.
- Assistant UI unit and Chromium component tests: rendering, navigation, page
  context, cancellation and conversation state.

The separate release certification workflow also runs the MCP contract, packaged
external-consumer and vertical-host gates described in [the MCP guide](../mcp/README.md).
Vertical tests use disposable databases and authentication servers.

## Native model regression suite

Run this separately against local Ollama with the default `qwen3.5:9b` installed:

```sh
dotnet test NGB.AiAssistant.AspNetCore.IntegrationTests/NGB.AiAssistant.AspNetCore.IntegrationTests.csproj \
  --environment NGB_RUN_OLLAMA_TESTS=1 \
  --logger 'trx;LogFileName=assistant-native.trx'
```

This uses the real model and MCP transport, with strict application-service
fixtures. It does not read production data. A run without `NGB_RUN_OLLAMA_TESTS=1`
skips native tests and is not evidence that answer-quality checks passed.

To compare another installed model, add `--environment NGB_OLLAMA_TEST_MODEL=qwen3.5:4b`.
The optional `NGB_OLLAMA_TEST_THINK=true` or `false` overrides thinking only for
that test process. Neither variable changes the application's configuration.
Run model comparisons sequentially on the same fixtures; record correctness,
per-test duration and retries, and account for model loading and prompt caching.
Faster simple answers do not qualify a mode for multi-step report queries.

The reported-regression tests include finding a payment without requesting a
link, a selected lease's rent, a five-lease table with exact decimal amounts,
catalog/document type names, and an English formatting follow-up after an
incorrectly German answer. Their test output includes model rounds and timings.
Conversation checks retain the original measure and population through clarifications,
including when the available capabilities cannot supply the requested result.
The unavailable-scope regression inspects model-requested operations as well as
executed service calls: an incorrect report attempt blocked by the read budget
must still fail the test. Complete report discovery should end an unsupported
aggregate request without substitute records, redundant pages or internal flags.
Application logs record each model round's duration, token usage and finish
reason without logging the question, answer or business field values.

Deterministic regression tests also cover decimal grouping with spaces, Unicode
minus signs, currency labels next to Markdown emphasis, and stale display
references from earlier turns. Failed reads cannot publish verified display
values or navigation sources. An invalid answer gets at most one shared evidence
correction attempt; correct localized formatting does not require another call.
Calendar dates written with an English month name bind to the same verified
date in month-first or day-first order, including a period after the day.
If a reasoning response exhausts its output budget, its one existing recovery
attempt requests no reasoning and no further reads, using retained evidence.
Normal discovery and reads keep the provider's reasoning mode. Unit tests cover
the transition and retained evidence; provider contracts verify Ollama's native
`think: false` mapping for that request.

Explicit report page limits retain the existing MCP service's validation. The
assistant reduces a page only when its actual response exceeds the evidence
budget, retaining the selection, cursor and completeness indicators. Tests cover
both a complete small result with a requested limit above 20 and oversized pages
that must be reduced. This does not add report filters, calculations or business
capabilities.

Record reads include the type name and direct lookup declarations from the metadata
already read by MCP. The adapter preserves declared target kinds and types, including
polymorphic targets, and does not infer a target from a bare UUID or field name.
Unit and HTTP tests verify the declarations survive transport without extra business
service reads or invented navigation sources. Native scenarios distinguish a direct
reference from unrelated records that only share a parent.

Keep the full suite green after changing instructions, projections or provider
configuration; a single repaired example is insufficient. Preserve failed-run
evidence and do not weaken factual assertions to accept an incorrect answer.
Record the model/version alongside results when qualifying a deployment; results
for one model do not qualify a different model or provider.

Numeric validation rejects unsupported decimal/currency claims before publishing
the draft and allows one correction without further reads. It preserves source
precision and does not substitute guessed values. It is not a proof of semantic
correctness: a value present elsewhere in the evidence can still be attributed
to the wrong record. Native scope and current-record tests cover that separately.

Production acceptance also depends on the deployed configuration and the user's
data checks. Passing these tests does not establish new business capabilities or
certify every possible natural-language question.
