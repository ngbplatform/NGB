#!/usr/bin/env node

import { runCli } from './ngb/cli.mjs'

await runCli(process.argv.slice(2))
