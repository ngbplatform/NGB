import { initializeConfiguration } from './configuration.mjs'

await initializeConfiguration(new URL('../.env', import.meta.url), process.argv[2])
console.log('Created private .env configuration. Read BOOTSTRAP_PASSWORD locally to sign in. Keep this file out of source control.')
