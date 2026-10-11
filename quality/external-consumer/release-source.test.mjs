import assert from 'node:assert/strict'
import test from 'node:test'
import { createRequire } from 'node:module'

const { assertCertification, selectReleaseSource } = createRequire(import.meta.url)('../../.github/scripts/release-source.cjs')
const sha = 'a'.repeat(40)

function fixture(eventName = 'workflow_dispatch') {
  const run = {
    id: 42, status: 'completed', conclusion: 'success', event: 'push', head_branch: 'main', head_sha: sha,
    path: '.github/workflows/external-app-certification.yml',
    repository: { full_name: 'owner/NGB' }, head_repository: { full_name: 'owner/NGB' },
  }
  const state = {
    runs: [{ ...run, id: 41 }, { ...run, event: 'pull_request', id: 50 }, run],
    artifacts: [{ id: 17, name: 'ngb-certified-release', expired: false, workflow_run: { head_sha: sha } }],
    environment: { protection_rules: [{ type: 'wait_timer' }, { type: 'required_reviewers', reviewers: [{ id: 1 }] }] },
  }
  const context = { eventName, repo: { owner: 'owner', repo: 'NGB' }, ref: 'refs/heads/main', sha, payload: { workflow_run: run } }
  const github = {
    paginate: async (method, parameters) => {
      if (method === 'runs') {
        assert.equal(parameters.workflow_id, 'external-app-certification.yml')
        assert.equal(parameters.head_sha, context.sha)
        return state.runs
      }
      return state.artifacts
    },
    rest: {
      actions: { listWorkflowRuns: 'runs', listWorkflowRunArtifacts: 'artifacts', getWorkflowRun: async ({ run_id }) => {
        assert.equal(run_id, 42)
        return { data: run }
      } },
      repos: { getEnvironment: async () => ({ data: state.environment }) },
    },
  }
  return { run, state, github, context }
}

test('automatic and manual publication select exact trusted artifacts without an operator-supplied ID', async () => {
  for (const event of ['workflow_dispatch', 'workflow_run']) {
    const { github, context } = fixture(event)
    assert.deepEqual(await selectReleaseSource(github, context), { runId: '42', sha, artifactId: '17' })
  }
})

test('forks, PRs, wrong commits, incomplete and failed certification cannot publish', () => {
  for (const override of [
    { repository: { full_name: 'other/NGB' } }, { head_repository: { full_name: 'fork/NGB' } },
    { head_branch: 'feature' }, { head_sha: 'b'.repeat(40) }, { path: '.github/workflows/other.yml' },
    { status: 'in_progress' }, { conclusion: 'failure' }, { event: 'pull_request' },
  ]) {
    assert.throws(() => assertCertification({ ...fixture().run, ...override }, 'owner/NGB', sha))
  }
})

test('selection fails closed for missing artifacts, approvals and unsupported events', async () => {
  for (const change of [
    value => { value.context.eventName = 'pull_request' },
    value => { value.context.ref = 'refs/heads/feature' },
    value => { value.state.runs = [] },
    value => { value.context.sha = 'invalid' },
    value => { value.state.artifacts = [] },
    value => { value.state.artifacts[0].expired = true },
    value => { value.state.artifacts[0].name = 'other' },
    value => { value.state.artifacts.push(value.state.artifacts[0]) },
    value => { value.state.artifacts[0].workflow_run.head_sha = 'b'.repeat(40) },
    value => { value.state.environment.protection_rules = [] },
    value => { value.state.environment.protection_rules[1].reviewers = [] },
  ]) {
    const value = fixture()
    change(value)
    await assert.rejects(selectReleaseSource(value.github, value.context))
  }
})
