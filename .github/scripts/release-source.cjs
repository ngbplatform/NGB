const assert = require('node:assert/strict')

const buildWorkflow = '.github/workflows/platform-packages.yml'

function assertPackageBuild(run, repository, sha) {
  assert.equal(run.repository.full_name, repository, 'Package build belongs to another repository.')
  assert.equal(run.head_repository.full_name, repository, 'Fork package build cannot authorize publication.')
  assert.equal(run.head_branch, 'main', 'Package build must run on main.')
  assert.equal(run.head_sha, sha, 'Package build must match the exact release commit.')
  assert.equal(run.path, buildWorkflow, 'Unexpected package build workflow.')
  assert.equal(run.status, 'completed', 'Package build is still running.')
  assert.equal(run.conclusion, 'success', 'Package build did not pass.')
  assert.ok(['push', 'workflow_dispatch'].includes(run.event), 'PR package build cannot authorize publication.')
  assert.ok(Number.isSafeInteger(run.run_attempt) && run.run_attempt > 0, 'Invalid build attempt.')
}

async function selectReleaseSource(github, context) {
  const repository = `${context.repo.owner}/${context.repo.repo}`
  let sha
  let runId

  if (context.eventName === 'workflow_run') {
    const trigger = context.payload.workflow_run
    sha = trigger.head_sha
    assertPackageBuild(trigger, repository, sha)
    runId = trigger.id
  } else {
    assert.equal(context.eventName, 'workflow_dispatch', 'Unsupported publication event.')
    assert.equal(context.ref, 'refs/heads/main', 'Dispatch publication from main.')
    sha = context.sha
    const runs = await github.paginate(github.rest.actions.listWorkflowRuns, {
      ...context.repo,
      workflow_id: 'platform-packages.yml',
      head_sha: sha,
      branch: 'main',
      status: 'success',
      per_page: 100,
    })
    const eligible = runs.filter(run => ['push', 'workflow_dispatch'].includes(run.event))
    assert.ok(eligible.length > 0, 'No successful main package build exists for this exact commit.')
    runId = eligible.sort((left, right) => right.id - left.id)[0].id
  }

  assert.match(sha, /^[a-f0-9]{40}$/, 'Invalid release commit.')
  const { data: run } = await github.rest.actions.getWorkflowRun({ ...context.repo, run_id: runId })
  assertPackageBuild(run, repository, sha)

  const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, {
    ...context.repo, run_id: run.id, per_page: 100,
  })
  const candidates = artifacts.filter(artifact => artifact.name === `ngb-platform-release-attempt-${run.run_attempt}` && !artifact.expired)
  assert.equal(candidates.length, 1, 'Exactly one unexpired package artifact from this build attempt is required; rerun the package build.')
  assert.equal(candidates[0].workflow_run.id, run.id, 'Artifact belongs to another build run.')
  assert.equal(candidates[0].workflow_run.head_sha, sha, 'Artifact belongs to another commit.')

  const { data: environment } = await github.rest.repos.getEnvironment({
    ...context.repo, environment_name: 'platform-release',
  })
  assert.ok(environment.protection_rules.some(rule => rule.type === 'required_reviewers' && rule.reviewers.length > 0),
    'Configure required reviewers for the platform-release environment before publication.')

  return { runId: String(run.id), runAttempt: String(run.run_attempt), sha, artifactId: String(candidates[0].id) }
}

module.exports = { assertPackageBuild, selectReleaseSource }
