import assert from 'node:assert/strict'
import { posix } from 'node:path'

export function verifyGuidance(payload) {
  const files = new Map(payload.files.map(file => [file.path, file.content]))
  const skills = payload.files.filter(file => /^ai\/vendor\/skills\/ngb-[a-z0-9-]+\/SKILL\.md$/.test(file.path))
  assert.ok(skills.length > 0, 'The Kit has no discoverable skills.')
  const names = []
  for (const skill of skills) {
    const name = skill.path.split('/')[3]
    names.push(name)
    assert.ok(skill.content.startsWith(`---\nname: ${name}\n`), `Invalid skill name: ${skill.path}`)
    const end = skill.content.indexOf('\n---\n', 4)
    assert.ok(end > 0 && end <= 1024, `Missing or excessive skill frontmatter: ${skill.path}`)
    assert.match(skill.content.slice(4, end), /\ndescription: [^\n]{20,300}$/,
      `A skill needs a concise trigger-focused description: ${skill.path}`)
    assert.ok(Buffer.byteLength(skill.content) <= 8192, `Move detailed guidance into a reference: ${skill.path}`)
    for (const agent of ['agents', 'claude']) {
      assert.equal(files.get(`.${agent}/skills/${name}/SKILL.md`),
        skill.content.replaceAll('../../references/', '../../../ai/vendor/references/'),
        `Agent adapter differs from its canonical semantic source: ${agent}/${name}`)
    }
  }
  let links = 0
  for (const [path, content] of files) {
    if (!path.endsWith('.md')) continue
    assert.ok(!content.includes('{{references}}'), `Unrendered reference path: ${path}`)
    for (const match of content.matchAll(/\[[^\]\n]+\]\(([^)\n]+)\)/g)) {
      const target = match[1]
      if (target.startsWith('https://')) continue
      assert.ok(!/[\\?:\s]/.test(target) && !target.startsWith('/'), `Nonportable reference in ${path}: ${target}`)
      const [file, anchor, ...extra] = target.split('#')
      assert.equal(extra.length, 0, `Invalid reference anchor: ${target}`)
      const resolved = file ? posix.normalize(posix.join(posix.dirname(path), file)) : path
      assert.ok(files.has(resolved), `Broken Kit reference in ${path}: ${target}`)
      if (anchor) {
        const headings = [...files.get(resolved).matchAll(/^#{1,6} (.+)$/gm)]
          .map(heading => heading[1].toLowerCase().replace(/[^\p{L}\p{N} _-]/gu, '').replaceAll(' ', '-'))
        assert.ok(headings.includes(anchor), `Missing reference heading in ${path}: ${target}`)
      }
      links += 1
    }
  }
  return { skills: names.sort(), localLinks: links }
}
