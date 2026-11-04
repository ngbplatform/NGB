import assert from 'node:assert/strict'
import test from 'node:test'
import { codexFinalAnswer, createAgentOutputRedactor, redactAgentOutput } from './agent-output.mjs'

test('agent journals redact credentials across every UTF-8 chunk boundary without changing event structure', () => {
  const event = { type: 'tool_result', content: [
    'HTTP_PROXY=http://sandbox-user:ephemeral-pass@127.0.0.1:1234',
    'ALL_PROXY=socks5h://encoded%40user:encoded%3Apassword@[::1]:1234',
    'Authorization: Bearer synthetic-token',
    'Proxy-Authorization: Basic c3ludGhldGljOnNlY3JldA==',
    'CLOUDSDK_PROXY_USERNAME=synthetic-user',
    'Password=synthetic-password',
    'Проверка завершена',
  ], nested: { token: 'private-value', api_key: 'private-key', proxyUsername: 'private-user', enabled: true, absent: null }, input_tokens: 42 }
  const bytes = Buffer.from(JSON.stringify(event) + '\n')
  for (let split = 0; split <= bytes.length; split++) {
    const redactor = createAgentOutputRedactor()
    const output = redactor.write(bytes.subarray(0, split)) + redactor.write(bytes.subarray(split)) + redactor.end()
    const actual = JSON.parse(output)
    assert.equal(actual.type, event.type)
    assert.deepEqual(actual.content, [
      'HTTP_PROXY=http://[REDACTED]@127.0.0.1:1234',
      'ALL_PROXY=socks5h://[REDACTED]@[::1]:1234',
      'Authorization: Bearer [REDACTED]',
      'Proxy-Authorization: Basic [REDACTED]',
      'CLOUDSDK_PROXY_USERNAME=[REDACTED]',
      'Password=[REDACTED]',
      'Проверка завершена',
    ])
    assert.deepEqual(actual.nested, { token: '[REDACTED]', api_key: '[REDACTED]', proxyUsername: '[REDACTED]', enabled: true, absent: null })
    assert.equal(actual.input_tokens, 42)
    assert.equal(redactor.end(), '')
  }
})

test('journals persist complete lines promptly and sanitize stderr and truncated records', () => {
  const redactor = createAgentOutputRedactor()
  assert.equal(redactor.write(Buffer.from('ready\nhttp://u:')), 'ready\n')
  assert.equal(redactor.write(Buffer.from('p@localhost:4321\n\nnext')), 'http://[REDACTED]@localhost:4321\n\n')
  assert.equal(redactor.end(), 'next')

  for (const value of ['HTTP_PROXY=http://user:partial', '{"content":"http://user:partial']) {
    const partial = createAgentOutputRedactor()
    assert.equal(partial.write(Buffer.from(value)), '')
    const output = partial.end()
    assert.ok(output.includes('[REDACTED INCOMPLETE URL]'))
    assert.ok(!output.includes('user:partial'))
  }

  const json = createAgentOutputRedactor()
  assert.equal(json.write(Buffer.from('{"result":"https://user:password@example.test"}')), '')
  assert.equal(json.end(), '{"result":"https://[REDACTED]@example.test"}')
  assert.equal(redactAgentOutput(Buffer.from('https://user:password@example.test\n')), 'https://[REDACTED]@example.test\n')
  assert.equal(redactAgentOutput(''), '')
  assert.deepEqual(JSON.parse(redactAgentOutput(JSON.stringify({
    PATH: '/toolchain/bin', HTTPS_PROXY: 'http://user:password@localhost:4321', CLOUDSDK_PROXY_USERNAME: 'synthetic',
  }))), { PATH: '/toolchain/bin', HTTPS_PROXY: 'http://[REDACTED]@localhost:4321', CLOUDSDK_PROXY_USERNAME: '[REDACTED]' })
})

test('final answers come from the redacted event stream without a separate raw client output file', () => {
  const events = [
    { type: 'item.completed', item: { type: 'agent_message', text: 'Earlier progress' } },
    { type: 'item.completed', item: { type: 'agent_message', text: 'Proxy http://user:password@localhost:4321' } },
    { type: 'item.completed', item: { type: 'command_execution', aggregated_output: 'command output' } },
    { type: 'item.completed' },
    { type: 'turn.completed', usage: { input_tokens: 42 } },
  ]
  const safe = redactAgentOutput(events.map(event => JSON.stringify(event)).join('\n'))
  assert.equal(codexFinalAnswer(safe.split('\n').map(line => JSON.parse(line))), 'Proxy http://[REDACTED]@localhost:4321')
  assert.throws(() => codexFinalAnswer([]), /Missing final/)
  for (const text of ['', null, 42]) {
    assert.throws(() => codexFinalAnswer([{ type: 'item.completed', item: { type: 'agent_message', text } }]), /Missing final/)
  }
})
