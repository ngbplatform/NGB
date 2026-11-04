import assert from 'node:assert/strict'
import test from 'node:test'
import { verifyGuidance } from '../../ai-devkit/guidance.mjs'
import { preparePayload } from '../../ai-devkit/prepare.mjs'

function fixture(content = '[Details](../../references/details.md#supported-actions)\n[Same file](#workflow)\n[Official](https://example.com/docs)') {
  const source = `---\nname: ngb-example\ndescription: Implement the example workflow using its public contracts.\n---\n\n# Workflow\n${content}\n`
  return { files: [
    { path: 'ai/vendor/skills/ngb-example/SKILL.md', content: source },
    { path: '.agents/skills/ngb-example/SKILL.md', content: source.replaceAll('../../references/', '../../../ai/vendor/references/') },
    { path: '.claude/skills/ngb-example/SKILL.md', content: source.replaceAll('../../references/', '../../../ai/vendor/references/') },
    { path: 'ai/vendor/references/details.md', content: '# Supported actions\n' },
    { path: 'ai/vendor/contract.json', content: '{}' },
  ] }
}

test('every packaged skill and reference resolves independently from both agent directories', async () => {
  const result = verifyGuidance(await preparePayload())
  assert.equal(result.skills.length, 16)
  assert.ok(result.skills.includes('ngb-background-jobs'))
  assert.ok(result.localLinks > 50)
  assert.deepEqual(verifyGuidance(fixture()), { skills: ['ngb-example'], localLinks: 6 })
  assert.equal(verifyGuidance(fixture('[File](../../references/details.md#)')).localLinks, 3)
})

test('broken links, nonportable paths and absent sections fail preparation', () => {
  for (const link of ['[Missing](missing.md)', '[Bad](file:///tmp/source)', '[Bad](/absolute.md)',
    '[Bad](../../references/details.md#missing)', '[Bad](#workflow#other)',
    '[Unrendered]({{references}}/details.md)', '[Bad](path with spaces.md)']) {
    assert.throws(() => verifyGuidance(fixture(link)))
  }
})

test('skills require bounded descriptions, canonical names and identical adapter semantics', () => {
  assert.throws(() => verifyGuidance({ files: [] }), /discoverable/)
  for (const replacement of [text => text.replace('name: ngb-example', 'name: other'),
    text => text.replace('\n---\n', '\nmissing\n'), text => text.replace('description:', 'unknown:'),
    text => text + 'x'.repeat(8192)]) {
    const payload = fixture()
    payload.files[0].content = replacement(payload.files[0].content)
    assert.throws(() => verifyGuidance(payload))
  }
  const payload = fixture()
  payload.files[1].content += 'Divergent instruction.'
  assert.throws(() => verifyGuidance(payload), /adapter differs/)
})
