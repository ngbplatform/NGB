const assert = require('node:assert/strict')

const publicationWorkflows = new Set([
  '.github/workflows/publish-platform-release.yml',
  '.github/workflows/publish-platform-nuget.yml',
  '.github/workflows/publish-platform-ui.yml',
])

function publicationCommit(run, repository) {
  assert.equal(run.repository.full_name, repository, 'Publication belongs to another repository.')
  assert.equal(run.head_repository.full_name, repository, 'Fork publication cannot authorize images.')
  assert.equal(run.head_branch, 'main', 'Publication must run on main.')
  assert.ok(publicationWorkflows.has(run.path), 'Unexpected publication workflow.')
  assert.equal(run.status, 'completed', 'Publication is still running.')
  assert.equal(run.conclusion, 'success', 'Publication and registry verification must pass.')
  assert.ok(['workflow_run', 'workflow_dispatch'].includes(run.event), 'Unexpected publication event.')

  // A workflow_run publication can have a newer head_sha than its certified
  // source. The selected commit is also checked against the downloaded manifest.
  const title = /^Publish NGB ([a-f0-9]{40})$/.exec(run.display_title)
  assert.ok(title, 'Publication must identify its exact release commit.')
  return title[1]
}

async function selectContainerSource(github, context) {
  const repository = `${context.repo.owner}/${context.repo.repo}`
  let runId
  let sha

  if (context.eventName === 'workflow_run') {
    const trigger = context.payload.workflow_run
    sha = publicationCommit(trigger, repository)
    runId = trigger.id
  } else {
    assert.equal(context.eventName, 'workflow_dispatch', 'Images start after publication or a manual retry.')
    assert.equal(context.ref, 'refs/heads/main', 'Dispatch container images from main.')
    sha = context.sha
    assert.match(sha, /^[a-f0-9]{40}$/, 'Invalid source commit.')
    const runs = await github.paginate(github.rest.actions.listWorkflowRunsForRepo, {
      ...context.repo, branch: 'main', status: 'success', per_page: 100,
    })
    const eligible = runs.filter(run => publicationWorkflows.has(run.path)
      && ['workflow_run', 'workflow_dispatch'].includes(run.event)
      && run.display_title === `Publish NGB ${sha}`)
    assert.ok(eligible.length > 0,
      'No verified publication exists for this commit. Wait for publication or rerun the container workflow for the released commit.')
    runId = eligible.sort((left, right) => right.id - left.id)[0].id
  }

  const { data: run } = await github.rest.actions.getWorkflowRun({ ...context.repo, run_id: runId })
  assert.equal(publicationCommit(run, repository), sha, 'Publication source changed.')
  const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, {
    ...context.repo, run_id: run.id, per_page: 100,
  })
  const evidence = artifacts.filter(artifact => artifact.name === 'ngb-publication-evidence' && !artifact.expired)
  assert.equal(evidence.length, 1, 'Exactly one unexpired publication evidence artifact is required.')
  assert.equal(evidence[0].workflow_run.id, run.id, 'Evidence belongs to another publication run.')
  assert.equal(evidence[0].workflow_run.head_sha, run.head_sha, 'Evidence publication metadata differs.')

  return { sha, runId: String(run.id), artifactId: String(evidence[0].id) }
}

function assertContainerManifest(manifest, sha, platformProps, uiPackage) {
  assert.equal(manifest.schemaVersion, 1, 'Unknown release manifest format.')
  assert.equal(manifest.sourceCommit, sha, 'Manifest belongs to another source commit.')
  assert.match(manifest.version, /^\d+\.\d+\.\d+$/, 'Invalid release version.')
  const versions = [...platformProps.matchAll(/<Version>([^<]+)<\/Version>/g)]
  assert.equal(versions.length, 1, 'Exactly one platform version is required.')
  assert.equal(versions[0][1], manifest.version, 'Backend version differs from the verified release.')
  assert.equal(uiPackage.dependencies['@ngbplatform/ui'], manifest.version,
    'CRM UI version differs from the verified release.')
}

module.exports = { publicationCommit, selectContainerSource, assertContainerManifest }
