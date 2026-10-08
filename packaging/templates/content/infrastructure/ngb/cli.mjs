import { resolve } from 'node:path'
import { argumentsFor } from './contracts.mjs'
import { createApplication, startApplication, upgradeApplication } from './application.mjs'

const help = `NGB application commands:

  node ngb.mjs create MyApp --local --start --email administrator@example.com
  node ngb.mjs create MyApp --version 3.2.0 --start
  node ngb.mjs upgrade --app ../MyApp --to 3.2.0
  node ngb.mjs upgrade --app ../MyApp --to 3.2.0 --local --apply

Inside a generated application:
  node infrastructure/ngb.mjs start
  node infrastructure/ngb.mjs upgrade --to 3.2.0 --apply
  node infrastructure/ngb.mjs deploy --backup-confirmed

--local uses the repository's artifacts/release-candidate by default.
--packages /path/to/candidate selects another manifest-bound local package set.
--output /path/to/MyApp selects a new, nonexistent destination for create.
Upgrade without --apply only displays the plan. Applying validates in an isolated
copy and changes dependency files only after both builds pass. Database migration
is a separate deploy operation and requires a consistent, tested backup.
`

export async function runCli(args, defaults = {}) {
  try {
    const options = argumentsFor(args)
    const settings = {
      ...defaults,
      outputDirectory: defaults.outputDirectory ?? process.cwd(),
      packagesDirectory: resolve(options.packages ?? defaults.packagesDirectory ?? 'artifacts/release-candidate'),
    }
    const root = resolve(options.app ?? process.cwd())
    if (options.command === 'help') console.log(help)
    else if (options.command === 'create') await createApplication(options, settings)
    else if (options.command === 'upgrade') await upgradeApplication(root, options, settings)
    else if (options.command === 'start') await startApplication(root, false)
    else {
      if (!options['backup-confirmed']) throw new Error('Take and test a consistent backup, then pass --backup-confirmed to deploy.')
      await startApplication(root, true)
    }
  } catch (error) {
    console.error(`NGB: ${error.message}`)
    process.exitCode = 1
  }
}
