import assert from 'node:assert/strict'

export function assertLayering(projects) {
  const allowed = {
    Definitions: [],
    Runtime: ['Definitions'],
    PostgreSql: ['Definitions'],
  }
  for (const project of projects) {
    const layer = project.name.split('.').at(-1)
    if (!Object.hasOwn(allowed, layer)) continue
    for (const reference of project.projects) {
      const dependency = reference.split('.').at(-1)
      assert.ok(allowed[layer].includes(dependency), `${project.name} must not reference ${reference}.`)
    }
    for (const dependency of project.packages) {
      const provider = /(?:PostgreSql|Dapper|Npgsql|EntityFrameworkCore)/i.test(dependency)
      const host = /(?:AspNetCore|\.Api$|\.Hosting|\.Migrator|\.Watchdog)/i.test(dependency)
      const runtime = /(?:\.Runtime|\.BackgroundJobs)/i.test(dependency)
      assert.ok(!host, `${project.name} must not depend on hosting package ${dependency}.`)
      assert.ok(layer === 'PostgreSql' || !provider, `${project.name} must not depend on provider package ${dependency}.`)
      assert.ok(layer === 'Runtime' || !runtime, `${project.name} must not depend on runtime package ${dependency}.`)
    }
  }
}
