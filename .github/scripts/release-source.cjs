const assert = require('node:assert/strict')

const certificationWorkflow = '.github/workflows/external-app-certification.yml'

function assertCertification(run, repository, sha) {
  assert.equal(run.repository.full_name, repository, 'Certification belongs to another repository.')
  assert.equal(run.head_repository.full_name, repository, 'Fork certification cannot authorize publication.')
  assert.equal(run.head_branch, 'main', 'Certification must run on main.')
  assert.equal(run.head_sha, sha, 'Certification must match the exact release commit.')
  assert.equal(run.path, certificationWorkflow, 'Unexpected certification workflow.')
  assert.equal(run.status, 'completed', 'Certification is still running.')
  assert.equal(run.conclusion, 'success', 'Certification did not pass.')
  assert.ok(['push', 'workflow_dispatch'].includes(run.event), 'PR certification cannot authorize publication.')
}

async function selectReleaseSource(github, context) {
  const repository = `${context.repo.owner}/${context.repo.repo}`
  let sha
  let runId

  if (context.eventName === 'workflow_run') {
    const trigger = context.payload.workflow_run
    sha = trigger.head_sha
    assertCertification(trigger, repository, sha)
    runId = trigger.id
  } else {
    assert.equal(context.eventName, 'workflow_dispatch', 'Unsupported publication event.')
    assert.equal(context.ref, 'refs/heads/main', 'Dispatch publication from main.')
    sha = context.sha
    const runs = await github.paginate(github.rest.actions.listWorkflowRuns, {
      ...context.repo,
      workflow_id: 'external-app-certification.yml',
      head_sha: sha,
      branch: 'main',
      status: 'success',
      per_page: 100,
    })
    const eligible = runs.filter(run => ['push', 'workflow_dispatch'].includes(run.event))
    assert.ok(eligible.length > 0, 'No successful main certification exists for this exact commit.')
    runId = eligible.sort((left, right) => right.id - left.id)[0].id
  }

  assert.match(sha, /^[a-f0-9]{40}$/, 'Invalid release commit.')
  const { data: run } = await github.rest.actions.getWorkflowRun({ ...context.repo, run_id: runId })
  assertCertification(run, repository, sha)

  const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, {
    ...context.repo, run_id: run.id, per_page: 100,
  })
  const candidates = artifacts.filter(artifact => artifact.name === 'ngb-certified-release' && !artifact.expired)
  assert.equal(candidates.length, 1, 'Exactly one unexpired certified artifact is required; rerun certification.')
  assert.equal(candidates[0].workflow_run.head_sha, sha, 'Artifact belongs to another commit.')

  const { data: environment } = await github.rest.repos.getEnvironment({
    ...context.repo, environment_name: 'platform-release',
  })
  assert.ok(environment.protection_rules.some(rule => rule.type === 'required_reviewers' && rule.reviewers.length > 0),
    'Configure required reviewers for the platform-release environment before publication.')

  return { runId: String(run.id), sha, artifactId: String(candidates[0].id) }
}

module.exports = { assertCertification, selectReleaseSource }
