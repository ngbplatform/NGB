import { randomBytes } from 'node:crypto'
import { writeFile } from 'node:fs/promises'

export async function initializeConfiguration(path, email) {
  if (!email || !/^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(email)) {
    throw new Error('Usage: node infrastructure/configure.mjs administrator@example.com')
  }
  const values = [
    `BOOTSTRAP_EMAIL=${email}`,
    `BOOTSTRAP_PASSWORD=${randomBytes(32).toString('base64url')}`,
    `POSTGRES_PASSWORD=${randomBytes(32).toString('base64url')}`,
    `KEYCLOAK_ADMIN_CLIENT_SECRET=${randomBytes(32).toString('base64url')}`,
  ]
  await writeFile(path, `${values.join('\n')}\n`, { mode: 0o600, flag: 'wx' })
}
