import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import test from 'node:test'

const { publicationCommit, selectContainerSource, assertContainerManifest } =
  createRequire(import.meta.url)('../../.github/scripts/container-source.cjs')
const sha = 'a'.repeat(40)
const newerSha = 'b'.repeat(40)
const workflowPaths = ['publish-platform-release', 'publish-platform-nuget', 'publish-platform-ui']
  .map(name => `.github/workflows/${name}.yml`)

function fixture(eventName = 'workflow_run') {
  const run = {
    id: 42, run_attempt: 2, status: 'completed', conclusion: 'success', event: 'workflow_run',
    head_branch: 'main', head_sha: newerSha, display_title: `Publish NGB ${sha}`,
    path: workflowPaths[0], repository: { full_name: 'owner/NGB' }, head_repository: { full_name: 'owner/NGB' },
  }
  const state = {
    runs: [
      { ...run, id: 41 },
      { ...run, id: 51, path: '.github/workflows/untrusted.yml' },
      { ...run, id: 52, event: 'pull_request' },
      { ...run, id: 53, display_title: `Publish NGB ${newerSha}` },
      run,
    ],
    artifacts: [{
      id: 17, name: 'ngb-publication-evidence-attempt-2', expired: false,
      created_at: '2026-10-11T20:10:15Z', workflow_run: { id: 42, head_sha: newerSha },
    }],
    jobs: [{
      run_id: 42, head_sha: newerSha, status: 'completed', conclusion: 'success',
      steps: [{
        name: 'Record publication and promotion evidence', status: 'completed', conclusion: 'success',
        started_at: '2026-10-11T20:10:10Z', completed_at: '2026-10-11T20:10:20Z',
      }],
    }],
  }
  const context = {
    eventName, repo: { owner: 'owner', repo: 'NGB' }, ref: 'refs/heads/main',
    sha: eventName === 'workflow_run' ? newerSha : sha, payload: { workflow_run: structuredClone(run) },
  }
  const github = {
    paginate: async (method, parameters) => {
      assert.equal(parameters.owner, 'owner')
      assert.equal(parameters.repo, 'NGB')
      if (method === 'runs') {
        assert.equal(parameters.branch, 'main')
        assert.equal(parameters.status, 'success')
        // A workflow_run publication's head_sha may be newer than its package source.
        assert.equal(parameters.head_sha, undefined)
        return state.runs
      }
      assert.equal(parameters.run_id, 42)
      if (method === 'jobs') {
        assert.equal(parameters.attempt_number, 2)
        return state.jobs
      }
      assert.equal(method, 'artifacts')
      return state.artifacts
    },
    rest: {
      actions: {
        listWorkflowRunsForRepo: 'runs', listWorkflowRunArtifacts: 'artifacts',
        listJobsForWorkflowRunAttempt: 'jobs',
        getWorkflowRun: async ({ run_id }) => {
          assert.equal(run_id, 42)
          return { data: run }
        },
      },
    },
  }
  return { run, state, github, context }
}

function legacyFixture(eventName = 'workflow_run') {
  const value = fixture(eventName)
  value.state.artifacts[0].name = 'ngb-publication-evidence'
  value.state.artifacts.unshift({
    ...value.state.artifacts[0], id: 16, created_at: '2026-10-11T19:50:00Z',
  })
  return value
}

test('automatic container builds retain the published commit when main advances', async () => {
  const value = fixture()
  assert.notEqual(value.context.sha, sha)
  assert.notEqual(value.run.head_sha, sha)
  assert.deepEqual(await selectContainerSource(value.github, value.context), { sha, runId: '42', artifactId: '17' })
})

test('all publication entry points and manual retries select exact successful evidence', async () => {
  for (const path of workflowPaths) {
    for (const eventName of ['workflow_run', 'workflow_dispatch']) {
      const value = fixture(eventName)
      value.run.path = path
      if (path !== workflowPaths[0]) value.run.event = 'workflow_dispatch'
      value.context.payload.workflow_run = structuredClone(value.run)
      assert.deepEqual(await selectContainerSource(value.github, value.context), { sha, runId: '42', artifactId: '17' })
    }
  }
})

test('publication retries select the current attempt and preserve older diagnostics', async () => {
  const value = fixture()
  value.state.artifacts.push(
    { ...value.state.artifacts[0], id: 18, name: 'ngb-publication-evidence-attempt-1' },
    { ...value.state.artifacts[0], id: 19, name: 'ngb-publication-evidence' },
  )
  value.state.jobs = []
  assert.deepEqual(await selectContainerSource(value.github, value.context), { sha, runId: '42', artifactId: '17' })
  assert.equal(value.state.artifacts.length, 3)
})

test('legacy duplicate archives are matched to the successful attempt upload step', async () => {
  for (const eventName of ['workflow_run', 'workflow_dispatch']) {
    const value = legacyFixture(eventName)
    value.state.artifacts.push({
      ...value.state.artifacts[0], id: 99, created_at: '2026-10-11T20:30:00Z',
    })
    assert.deepEqual(await selectContainerSource(value.github, value.context), { sha, runId: '42', artifactId: '17' })
    value.state.artifacts.reverse()
    assert.deepEqual(await selectContainerSource(value.github, value.context), { sha, runId: '42', artifactId: '17' })
  }
})

test('legacy artifact times respect upload boundaries and GitHub timestamp precision', async () => {
  for (const createdAt of ['2026-10-11T20:10:10Z', '2026-10-11T20:10:20.999Z']) {
    const value = legacyFixture()
    value.state.artifacts[1].created_at = createdAt
    assert.deepEqual(await selectContainerSource(value.github, value.context), { sha, runId: '42', artifactId: '17' })
  }
})

test('legacy evidence cannot fall back to another attempt or ambiguous upload metadata', async () => {
  for (const change of [
    value => { value.state.jobs = [] },
    value => { value.state.jobs.push(value.state.jobs[0]) },
    value => { value.state.jobs[0].run_id = 43 },
    value => { value.state.jobs[0].head_sha = sha },
    value => { value.state.jobs[0].status = 'in_progress' },
    value => { value.state.jobs[0].conclusion = 'failure' },
    value => { value.state.jobs[0].steps = [] },
    value => { value.state.jobs[0].steps[0].name = 'Other upload' },
    value => { value.state.jobs[0].steps[0].status = 'in_progress' },
    value => { value.state.jobs[0].steps[0].conclusion = 'failure' },
    value => { value.state.jobs[0].steps[0].started_at = null },
    value => { value.state.jobs[0].steps[0].completed_at = null },
    value => { value.state.jobs[0].steps[0].started_at = '2026-10-11T20:11:00Z' },
    value => { value.state.artifacts[1].created_at = 'invalid' },
    value => { value.state.artifacts[1].created_at = '2026-10-11T20:10:09.999Z' },
    value => { value.state.artifacts[1].created_at = '2026-10-11T20:10:21Z' },
    value => { value.state.artifacts[1].expired = true },
    value => { value.state.artifacts[1].workflow_run.id = 43 },
    value => { value.state.artifacts[1].workflow_run.head_sha = sha },
    value => { value.state.artifacts.push({ ...value.state.artifacts[1], id: 18 }) },
    value => { value.state.artifacts.push({ ...value.state.artifacts[1], name: 'ngb-publication-evidence-attempt-2', expired: true }) },
  ]) {
    const value = legacyFixture()
    change(value)
    await assert.rejects(selectContainerSource(value.github, value.context))
  }
})

test('failed, cancelled, incomplete, foreign and untrusted publication cannot start images', async () => {
  for (const override of [
    { repository: { full_name: 'other/NGB' } }, { head_repository: { full_name: 'fork/NGB' } },
    { head_branch: 'feature/release' }, { path: '.github/workflows/external-app-certification.yml' },
    { status: 'in_progress' }, { conclusion: 'failure' }, { conclusion: 'cancelled' },
    { event: 'pull_request' }, { display_title: 'Publish NGB main' },
  ]) {
    const value = fixture()
    Object.assign(value.run, override)
    assert.throws(() => publicationCommit(value.run, 'owner/NGB'))
    await assert.rejects(selectContainerSource(value.github, value.context))
    value.context.payload.workflow_run = structuredClone(value.run)
    await assert.rejects(selectContainerSource(value.github, value.context))
  }
})

test('wrong or unavailable publication evidence and unsupported dispatches fail closed', async () => {
  for (const change of [
    value => { value.context.eventName = 'push' },
    value => { value.context.eventName = 'workflow_dispatch'; value.context.ref = 'refs/heads/feature/test' },
    value => { value.context.eventName = 'workflow_dispatch'; value.context.sha = 'invalid' },
    value => { value.context.eventName = 'workflow_dispatch'; value.state.runs = [] },
    value => { value.run.display_title = `Publish NGB ${newerSha}` },
    value => { value.run.run_attempt = undefined },
    value => { value.run.run_attempt = 0 },
    value => { value.run.run_attempt = 1.5 },
    value => { value.state.artifacts = [] },
    value => { value.state.artifacts[0].expired = true },
    value => { value.state.artifacts[0].name = 'ngb-certified-release' },
    value => { value.state.artifacts[0].name = 'ngb-publication-evidence-attempt-1' },
    value => { value.state.artifacts.push(value.state.artifacts[0]) },
    value => { value.state.artifacts[0].workflow_run.id = 43 },
    value => { value.state.artifacts[0].workflow_run.head_sha = sha },
  ]) {
    const value = fixture()
    change(value)
    await assert.rejects(selectContainerSource(value.github, value.context))
  }
})

test('publication archives identify the attempt even when saved after a failure', async () => {
  const workflow = await readFile(new URL('../../.github/workflows/publish-platform-release.yml', import.meta.url), 'utf8')
  const upload = workflow.slice(workflow.indexOf('      - name: Record publication and promotion evidence'))
  assert.match(upload, /if: always\(\)/)
  assert.match(upload, /name: ngb-publication-evidence-attempt-\$\{\{ github.run_attempt \}\}/)
})

test('release manifest, backend and CRM UI must agree on source and exact version', () => {
  const manifest = { schemaVersion: 1, sourceCommit: sha, version: '3.2.0' }
  const props = '<Project><PropertyGroup><Version>3.2.0</Version></PropertyGroup></Project>'
  const ui = { dependencies: { '@ngbplatform/ui': '3.2.0' } }
  assertContainerManifest(manifest, sha, props, ui)
  for (const override of [{ schemaVersion: 2 }, { sourceCommit: newerSha }, { version: 'latest' }, { version: '3.1.0' }]) {
    assert.throws(() => assertContainerManifest({ ...manifest, ...override }, sha, props, ui))
  }
  assert.throws(() => assertContainerManifest(manifest, sha, '<Project />', ui))
  assert.throws(() => assertContainerManifest(manifest, sha, props + props, ui))
  assert.throws(() => assertContainerManifest(manifest, sha, props, { dependencies: { '@ngbplatform/ui': '^3.2.0' } }))
})

test('workflow orders evidence before every image and uses that source for deployment', async () => {
  const workflow = await readFile(new URL('../../.github/workflows/container-images.yml', import.meta.url), 'utf8')
  const trigger = workflow.slice(workflow.indexOf('on:\n'), workflow.indexOf('permissions:\n'))
  assert.match(trigger, /workflow_run:/)
  assert.match(trigger, /workflows: \[publish-certified-platform-release, publish-platform-nuget, publish-platform-ui\]/)
  assert.match(trigger, /types: \[completed\]/)
  assert.match(trigger, /branches: \[main\]/)
  assert.doesNotMatch(trigger, /\n  (push|pull_request):/)
  assert.match(workflow, /cancel-in-progress: false/)

  const jobs = Object.fromEntries([...workflow.matchAll(/^  ([a-z-]+):\n([\s\S]*?)(?=^  [a-z-]+:\n|$(?![\s\S]))/gm)]
    .map(match => [match[1], match[2]]))
  const source = jobs['release-source']
  assert.match(source, /github.event.workflow_run.conclusion == 'success'/)
  assert.match(source, /git merge-base --is-ancestor/)
  assert.match(source, /ref: \$\{\{ steps.source.outputs.sha \}\}/)
  assert.match(source, /assertContainerManifest\(manifest, process.env.NGB_RELEASE_COMMIT/)
  assert.match(source, /release.mjs verify-publication/)
  assert.match(source, /release.mjs verify-promotion/)
  assert.match(jobs['crm-registry-dependencies'], /needs: release-source/)
  assert.doesNotMatch(jobs['crm-registry-dependencies'], /available=false|Skipping CRM/)

  for (const name of ['build-platform', 'build-crm']) {
    const job = jobs[name]
    assert.match(job, /needs:\n      - release-source\n      - crm-registry-dependencies/)
    assert.match(job, /ref: \$\{\{ needs.release-source.outputs.sha \}\}/)
    assert.match(job, /:sha-\$\{\{ needs.release-source.outputs.sha \}\}/)
    assert.match(job, /org.opencontainers.image.revision=\$\{\{ needs.release-source.outputs.sha \}\}/)
    assert.match(job, /push: true/)
    assert.match(job, /cache-from: type=gha,scope=\$\{\{ matrix.image \}\}/)
    assert.match(job, /cache-to: type=gha,mode=max,scope=\$\{\{ matrix.image \}\},ignore-error=true/)
    assert.doesNotMatch(job, /continue-on-error:/)
    assert.doesNotMatch(job, /github.sha|github.ref_name|always\(\)/)
  }
  const deployment = jobs['propose-infra-deploy']
  assert.match(deployment, /needs.build-platform.result == 'success' &&/)
  assert.match(deployment, /needs.build-crm.result == 'success'/)
  assert.match(deployment, /NGB_SOURCE_SHA: \$\{\{ needs.release-source.outputs.sha \}\}/)
  assert.match(deployment, /IMAGE_TAG: sha-\$\{\{ needs.release-source.outputs.sha \}\}/)
  assert.doesNotMatch(deployment, /github.sha|always\(\)/)
})
