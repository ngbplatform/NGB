import { join } from 'node:path'

// Share downloaded bytes only. Each consumer still restores into a fresh package
// directory and npm ci validates its lockfile integrity before extracting modules.
export function dependencyCache(localRoot, scope, isolated = false) {
  const shared = isolated ? undefined : process.env.NGB_CERTIFICATION_CACHE
  return {
    npm: join(shared ?? localRoot, `npm-${scope}`),
    nugetHttp: join(shared ?? localRoot, `nuget-http-${scope}`),
  }
}
