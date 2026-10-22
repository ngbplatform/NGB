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

Run this separately against local Ollama with `qwen3.5:9b` installed:

```sh
dotnet test NGB.AiAssistant.AspNetCore.IntegrationTests/NGB.AiAssistant.AspNetCore.IntegrationTests.csproj \
  --environment NGB_RUN_OLLAMA_TESTS=1 \
  --logger 'trx;LogFileName=assistant-native.trx'
```

This uses the real model and MCP transport, with strict application-service
fixtures. It does not read production data. A run without `NGB_RUN_OLLAMA_TESTS=1`
skips native tests and is not evidence that answer-quality checks passed.

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
