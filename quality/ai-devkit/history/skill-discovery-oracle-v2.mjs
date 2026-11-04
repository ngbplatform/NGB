import assert from 'node:assert/strict'

// Frozen before inference. The response schema describes the answer format;
// it deliberately contains none of the expected versions, paths or outcomes.
export const responseSchema = {
  type: 'object',
  additionalProperties: false,
  required: ['sdkVersion', 'kitVersion', 'officialPath', 'customPath', 'documentSkill', 'zeroEffectPosting'],
  properties: {
    sdkVersion: { type: 'string' },
    kitVersion: { type: 'string' },
    officialPath: { type: 'string' },
    customPath: { type: 'string' },
    documentSkill: {
      type: 'object',
      additionalProperties: false,
      required: ['name', 'path'],
      properties: { name: { type: 'string' }, path: { type: 'string' } },
    },
    zeroEffectPosting: {
      type: 'object',
      additionalProperties: false,
      required: ['supported', 'errorCode', 'resultingStatus', 'effectsChanged'],
      properties: {
        supported: { type: 'boolean' },
        errorCode: { type: 'string' },
        resultingStatus: { type: 'string' },
        effectsChanged: { type: 'boolean' },
      },
    },
  },
}

export function verifySkillDiscovery({ answer, before, after, version, client }) {
  assert.ok(['codex', 'claude'].includes(client), 'Unknown discovery client.')
  assert.equal(answer.sdkVersion, version, 'The agent identified the wrong SDK version.')
  assert.equal(answer.kitVersion, version, 'The agent identified the wrong Kit version.')
  assert.ok(['ai/vendor', 'ai/vendor/instructions.md'].includes(answer.officialPath),
    'Official installed guidance was not discovered.')
  assert.ok(['ai/custom', 'ai/custom/instructions.md'].includes(answer.customPath),
    'Application-owned guidance was not discovered.')
  assert.equal(answer.documentSkill.name, 'ngb-documents', 'The document-development skill was not identified.')
  const adapter = client === 'codex' ? '.agents' : '.claude'
  assert.ok([`${adapter}/skills/ngb-documents/SKILL.md`, 'ai/vendor/skills/ngb-documents/SKILL.md']
    .includes(answer.documentSkill.path), 'The document skill path does not belong to this client or the canonical installed Kit.')
  assert.deepEqual(answer.zeroEffectPosting, {
    supported: false,
    errorCode: 'doc.posting.handler.not_configured',
    resultingStatus: 'Draft',
    effectsChanged: false,
  }, 'The agent must preserve the recorded public SDK limitation and unchanged Draft/effects.')
  assert.ok(Object.keys(before).length > 0, 'The independent fixture inventory is empty.')
  assert.deepEqual(after, before, 'Read-only discovery changed the fixture.')
  return ['correct-versions', 'official-and-custom-discovery', 'document-skill-discovery',
    'zero-effect-limitation', 'no-source-changes'].map(id => ({ id, state: 'passed' }))
}
