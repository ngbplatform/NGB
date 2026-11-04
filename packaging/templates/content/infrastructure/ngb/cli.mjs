import { resolve } from 'node:path'
import { argumentsFor, toolVersion } from './contracts.mjs'
import { createApplication, startApplication, upgradeApplication } from './application.mjs'
import { emitReport, report, runKit } from './kit.mjs'
import { doctor, executeProfile } from './profiles.mjs'
import { validateApplication } from './validation.mjs'
import { delegateUpgrade, needsDelegation, validateDelegation } from './delegation.mjs'
import { recoverTransaction } from './transaction.mjs'
import { recoverTestEnvironment } from './environment.mjs'

const help = `NGB application commands:

  node ngb.mjs create MyApp --local --start --email administrator@example.com
  node ngb.mjs create MyApp --version ${toolVersion} --start
  node ngb.mjs upgrade --app ../MyApp --to ${toolVersion}
  node ngb.mjs upgrade --app ../MyApp --to ${toolVersion} --local --apply

Inside a generated application:
  node infrastructure/ngb.mjs start
  node infrastructure/ngb.mjs upgrade --to ${toolVersion} --apply
  node infrastructure/ngb.mjs deploy --backup-confirmed
  node infrastructure/ngb.mjs ai status --format json
  node infrastructure/ngb.mjs ai sync
  node infrastructure/ngb.mjs ai sync --apply --resolutions reviewed-resolutions.json
  node infrastructure/ngb.mjs recover
  node infrastructure/ngb.mjs recover --apply
  node infrastructure/ngb.mjs recover --resources
  node infrastructure/ngb.mjs recover --resources --apply
  node infrastructure/ngb.mjs doctor --format json
  node infrastructure/ngb.mjs validate
  node infrastructure/ngb.mjs validate --profile build
  node infrastructure/ngb.mjs test --profile unit
  node infrastructure/ngb.mjs test --profile integration
  node infrastructure/ngb.mjs test --profile e2e
  node infrastructure/ngb.mjs certify --profile external-application

--local uses the repository's artifacts/release-candidate by default.
--packages /path/to/candidate selects another manifest-bound local package set.
--output /path/to/MyApp selects a new, nonexistent destination for create.
Registry commands require the target release to be published; use local packages before publication.
Upgrade without --apply only displays the plan. Applying validates in an isolated
copy and commits dependencies, matching CLI/helper and Kit together after builds pass. Database migration
is a separate deploy operation and requires a consistent, tested backup.
For targets newer than this CLI, upgrade acquires that exact official CLI outside
the application and delegates once. It never selects latest or runs install hooks.
After an interrupted update, inspect recover, then use recover --apply to restore
the source files. Recovery preserves conflicting subsequent edits and reports them.
After an interrupted test, recover --resources inspects the recorded test runs.
Add --apply to remove their owned resources after the owner and provisioner exit.

doctor, validate and ai status/sync preview inspect data without running application
code. validate --profile build, test and certify execute trusted project tooling.
JSON reports use schemaVersion 1. Exit codes: 0 passed, 1 invalid legacy command,
2 failed/not-run required check, 4 unresolved Kit conflict; interrupted upgrades
return 130 for SIGINT and 143 for SIGTERM. Missing tests or tools
never count as passing. Test/certification resources must be disposable.
`

export async function runCli(args, defaults = {}) {
  const cancellation = new AbortController()
  let interruptedExitCode = 130
  const interrupt = code => {
    if (cancellation.signal.aborted) return
    interruptedExitCode = code
    cancellation.abort()
  }
  const onInterrupt = () => interrupt(130)
  const onTerminate = () => interrupt(143)
  try {
    const options = argumentsFor(args)
    await validateDelegation(options)
    const settings = {
      ...defaults,
      outputDirectory: defaults.outputDirectory ?? process.cwd(),
      packagesDirectory: resolve(options.packages ?? defaults.packagesDirectory ?? 'artifacts/release-candidate'),
    }
    const root = resolve(options.app ?? process.cwd())
    if (needsDelegation(options)) {
      process.once('SIGINT', onInterrupt)
      process.once('SIGTERM', onTerminate)
      const result = await delegateUpgrade(args, options, settings, { signal: cancellation.signal })
      if (result.output) process.stdout.write(result.output)
      process.exitCode = cancellation.signal.aborted ? interruptedExitCode : result.exitCode ?? 2
      return
    }
    if (options.command === 'help') console.log(help)
    else if (options.command === 'create') await createApplication(options, settings)
    else if (options.command === 'upgrade') {
      process.once('SIGINT', onInterrupt)
      process.once('SIGTERM', onTerminate)
      await upgradeApplication(root, options, settings, cancellation.signal)
    }
    else if (options.command === 'start') await startApplication(root, false)
    else if (options.command === 'recover' && options.resources) {
      const recovery = await recoverTestEnvironment(root, options.apply)
      emitReport(report('recover', [{ id: 'resources.recovery', required: true, state: 'passed',
        message: `${recovery.state}: ${recovery.runs.length} recorded test runs.` }], { recovery }), options.format)
    }
    else if (options.command === 'recover') {
      const recovery = await recoverTransaction(root, options.apply)
      emitReport(report('recover', [{ id: 'transaction.recovery', required: true, state: 'passed',
        message: recovery.state === 'not-needed' ? 'No interrupted transaction.'
          : `${options.apply ? 'Completed' : 'Preview'}: ${recovery.action} transaction ${recovery.id}.` }], { recovery }), options.format)
    }
    else if (options.command === 'ai') emitReport(await runKit(root, options), options.format)
    else if (options.command === 'doctor') emitReport(await doctor(root), options.format)
    else if (options.command === 'validate' && !options.profile) emitReport(await validateApplication(root), options.format)
    else if (['validate', 'test', 'certify'].includes(options.command)) emitReport(await executeProfile(root, options), options.format)
    else {
      if (!options['backup-confirmed']) throw new Error('Take and test a consistent backup, then pass --backup-confirmed to deploy.')
      await startApplication(root, true)
    }
  } catch (error) {
    if (cancellation.signal.aborted) {
      process.exitCode = interruptedExitCode
      return
    }
    if (args.includes('--format') && args[args.indexOf('--format') + 1] === 'json' || args.includes('--format=json')) {
      emitReport(report('error', [{ id: 'cli.input', required: true, state: 'failed', message: error.message }]), 'json')
    } else {
      console.error(`NGB: ${error.message}`)
      process.exitCode = 1
    }
  } finally {
    process.removeListener('SIGINT', onInterrupt)
    process.removeListener('SIGTERM', onTerminate)
  }
}
