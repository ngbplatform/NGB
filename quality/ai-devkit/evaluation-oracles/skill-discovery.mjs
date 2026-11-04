import assert from 'node:assert/strict'

// Frozen before inference. The response schema describes the answer format;
// it deliberately contains none of the expected versions, paths or outcomes.
export const responseSchema = {
  type: 'object',
  additionalProperties: false,
  required: ['sdkVersion', 'kitVersion', 'officialPath', 'customPath', 'documentSkill', 'missingHandlerPosting', 'emptyHandlerPosting'],
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
    missingHandlerPosting: {
      type: 'object',
      additionalProperties: false,
      required: ['supported', 'errorCode', 'resultingStatus', 'effectsChanged'],
      properties: {
        supported: { type: 'boolean' },
        errorCode: { type: 'string' },
        resultingStatus: { type: 'string', description: 'Exact document status enum member; omit explanatory prose.' },
        effectsChanged: { type: 'boolean' },
      },
    },
    emptyHandlerPosting: {
      type: 'object',
      additionalProperties: false,
      required: ['supported', 'resultingStatus', 'effectsChanged', 'requiresHandlerRegistration'],
      properties: {
        supported: { type: 'boolean' },
        resultingStatus: { type: 'string', description: 'Exact document status enum member; omit explanatory prose.' },
        effectsChanged: { type: 'boolean' },
        requiresHandlerRegistration: { type: 'boolean' },
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
  assert.deepEqual(answer.missingHandlerPosting, {
    supported: false,
    errorCode: 'doc.posting.handler.not_configured',
    resultingStatus: 'Draft',
    effectsChanged: false,
  }, 'The agent must preserve the recorded public SDK limitation and unchanged Draft/effects.')
  assert.deepEqual(answer.emptyHandlerPosting, {
    supported: true,
    resultingStatus: 'Posted',
    effectsChanged: false,
    requiresHandlerRegistration: true,
  }, 'A registered handler can support successful Post without producing movements.')
  assert.ok(Object.keys(before).length > 0, 'The independent fixture inventory is empty.')
  assert.deepEqual(after, before, 'Read-only discovery changed the fixture.')
  return ['correct-versions', 'official-and-custom-discovery', 'document-skill-discovery',
    'missing-handler-refused', 'empty-handler-supported', 'no-source-changes'].map(id => ({ id, state: 'passed' }))
}
