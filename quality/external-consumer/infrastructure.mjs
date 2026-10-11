import { execFileSync } from 'node:child_process'
import { chmod, mkdir, writeFile } from 'node:fs/promises'
import { join } from 'node:path'

export async function writeInfrastructureConfiguration(directory, realm, email, secrets) {
  const configured = structuredClone(realm)
  configured.users[0].username = email
  configured.users[0].email = email
  configured.users[0].credentials[0].value = '${BOOTSTRAP_PASSWORD}'
  configured.clients[2].secret = '${KEYCLOAK_ADMIN_CLIENT_SECRET}'
  configured.clients.push({
    clientId: 'certification-harness', enabled: true, publicClient: false,
    secret: '${CERTIFICATION_HARNESS_SECRET}', directAccessGrantsEnabled: true,
    standardFlowEnabled: false, protocol: 'openid-connect',
  })

  // Bind-mounted inputs must be readable by the images' non-root users on Linux.
  // Only placeholders go in realm.json; actual secrets remain in the private .env.
  for (const [name, content] of [
    ['realm.json', `${JSON.stringify(configured, null, 2)}\n`],
    ['init.sql', 'CREATE DATABASE keycloak;\n'],
  ]) {
    const path = join(directory, name)
    await writeFile(path, content, { mode: 0o644, flag: 'wx' })
    await chmod(path, 0o644)
  }
  const values = [
    `POSTGRES_PASSWORD=${secrets.postgres}`,
    `BOOTSTRAP_PASSWORD=${secrets.administrator}`,
    `KEYCLOAK_ADMIN_CLIENT_SECRET=${secrets.client}`,
    `CERTIFICATION_HARNESS_SECRET=${secrets.harness}`,
    `MINIO_ROOT_PASSWORD=${secrets.minioRoot}`,
    `MINIO_APP_SECRET_KEY=${secrets.minioApp}`,
  ]
  await writeFile(join(directory, '.env'), `${values.join('\n')}\n`, { mode: 0o600, flag: 'wx' })
}

export async function saveInfrastructureDiagnostics(directory, composeArguments, secrets, execute = execFileSync) {
  const redact = text => Object.values(secrets).reduce((value, secret) => value.replaceAll(secret, '[REDACTED]'), text)
  const sections = []
  for (const args of [['ps', '--all'], ['logs', '--no-color', '--tail', '200']]) {
    sections.push(`$ docker compose ${args.join(' ')}`)
    try {
      sections.push(redact(execute('docker', [...composeArguments, ...args], {
        encoding: 'utf8', stdio: 'pipe', timeout: 15_000, maxBuffer: 2 * 1024 * 1024,
      })))
    } catch (error) {
      sections.push(redact(`Diagnostic collection failed: ${error.message}\n${error.stdout ?? ''}\n${error.stderr ?? ''}`))
    }
  }
  await mkdir(directory, { recursive: true })
  const path = join(directory, 'infrastructure.log')
  await writeFile(path, `${sections.join('\n')}\n`, { mode: 0o600 })
  return path
}
