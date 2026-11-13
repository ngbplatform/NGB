import assert from 'node:assert/strict'
import test from 'node:test'
import { providerUnavailable } from './agent-availability.mjs'

test('only native quota errors defer an incomplete inference', () => {
  const failure = 'Missing successful client output.'
  const event = value => JSON.stringify(value)
  assert.equal(providerUnavailable('codex', failure, event({ type: 'error', message: 'You have hit your usage limit.' })), true)
  assert.equal(providerUnavailable('codex', failure, event({ type: 'turn.failed', error: { code: 'rate_limit_exceeded' } })), true)
  assert.equal(providerUnavailable('codex', failure, event({ type: 'turn.failed', error: { message: 'insufficient_quota' } })), true)
  assert.equal(providerUnavailable('claude', failure, event({ type: 'result', subtype: 'error_during_execution', errors: ['overloaded_error'] })), true)
  assert.equal(providerUnavailable('claude', failure, event({ type: 'assistant', error: 'rate_limit' })), true)
  for (const client of ['codex', 'claude']) {
    for (const transcript of ['', 'partial invalid JSON',
      event({ type: 'item.completed', item: { text: 'usage limit' } }),
      event({ type: 'assistant', message: 'rate limit' }),
      event({ type: 'result', subtype: 'success', result: 'usage limit' }),
      event({ type: 'result', subtype: 'error_max_budget_usd', errors: ['usage limit'] }),
      event({ type: 'result', subtype: 'error_during_execution' }),
      event({ type: 'result', subtype: 'error_during_execution', errors: [123, 'unknown failure'] }),
      event({ type: 'turn.failed', error: { message: 'test failed' } }),
    ]) assert.equal(providerUnavailable(client, failure, transcript), false)
  }
  const quota = event({ type: 'error', message: 'usage limit' })
  for (const failure of ['Reported usage exceeded the frozen token acceptance ceiling.', 'Independent test failed.',
    'Agent process failed: timeout.', 'Source review rejected the output.']) {
    assert.equal(providerUnavailable('codex', failure, quota), false)
  }
})
