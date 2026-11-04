import assert from 'node:assert/strict'
import test from 'node:test'
import { responseSchema, verifySkillDiscovery } from './evaluation-oracles/skill-discovery.mjs'

function fixture() {
  return {
    version: '3.4.0',
    client: 'claude',
    before: { 'CLAUDE.md': 'fixture-hash' },
    after: { 'CLAUDE.md': 'fixture-hash' },
    answer: {
      sdkVersion: '3.4.0',
      kitVersion: '3.4.0',
      officialPath: 'ai/vendor',
      customPath: 'ai/custom',
      documentSkill: { name: 'ngb-documents', path: '.claude/skills/ngb-documents/SKILL.md' },
      missingHandlerPosting: {
        supported: false,
        errorCode: 'doc.posting.handler.not_configured',
        resultingStatus: 'Draft',
        effectsChanged: false,
      },
      emptyHandlerPosting: {
        supported: true,
        resultingStatus: 'Posted',
        effectsChanged: false,
        requiresHandlerRegistration: true,
      },
    },
  }
}

test('discovery oracle accepts both installed adapters and the canonical skill', () => {
  const value = fixture()
  assert.equal(verifySkillDiscovery(value).length, 6)
  value.client = 'codex'
  value.answer.documentSkill.path = '.agents/skills/ngb-documents/SKILL.md'
  assert.ok(verifySkillDiscovery(value).every(check => check.state === 'passed'))
  value.answer.documentSkill.path = 'ai/vendor/skills/ngb-documents/SKILL.md'
  assert.equal(verifySkillDiscovery(value).length, 6)
  value.answer.officialPath = 'ai/vendor/instructions.md'
  value.answer.customPath = 'ai/custom/instructions.md'
  assert.equal(verifySkillDiscovery(value).length, 6)
  assert.deepEqual(responseSchema.required.sort(), Object.keys(value.answer).sort())
  const schema = JSON.stringify(responseSchema)
  for (const expected of ['3.4.0', 'ai/vendor', 'ai/custom', 'ngb-documents', 'doc.posting.handler.not_configured']) {
    assert.ok(!schema.includes(expected), 'The response format must not supply the expected answer.')
  }
})

test('discovery oracle rejects wrong versions, ownership, skills, lifecycle semantics and fixture changes', () => {
  for (const change of [
    value => { value.client = 'other' },
    value => { value.answer.sdkVersion = '3.3.0' },
    value => { value.answer.kitVersion = '3.3.0' },
    value => { value.answer.officialPath = 'ai/custom' },
    value => { value.answer.customPath = 'ai/vendor' },
    value => { value.answer.documentSkill.name = 'ngb-catalogs' },
    value => { value.answer.documentSkill.path = '../outside/SKILL.md' },
    value => { value.answer.documentSkill.path = '.agents/skills/ngb-documents/SKILL.md' },
    value => { value.answer.missingHandlerPosting.supported = true },
    value => { value.answer.missingHandlerPosting.errorCode = 'invented.code' },
    value => { value.answer.missingHandlerPosting.resultingStatus = 'Posted' },
    value => { value.answer.missingHandlerPosting.effectsChanged = true },
    value => { value.answer.emptyHandlerPosting.supported = false },
    value => { value.answer.emptyHandlerPosting.resultingStatus = 'Draft' },
    value => { value.answer.emptyHandlerPosting.effectsChanged = true },
    value => { value.answer.emptyHandlerPosting.requiresHandlerRegistration = false },
    value => { value.before = {} },
    value => { value.after['CLAUDE.md'] = 'changed' },
    value => { value.after['new.md'] = 'unexpected' },
  ]) {
    const value = fixture()
    change(value)
    assert.throws(() => verifySkillDiscovery(value))
  }
})
