type RuntimeConfigValue = string | boolean | number | null | undefined

declare global {
  interface Window {
    __NGB_RUNTIME_CONFIG__?: Record<string, RuntimeConfigValue>
  }
}

function readWindowRuntimeConfig(): Record<string, RuntimeConfigValue> | null {
  if (typeof window === 'undefined') return null

  const runtimeConfig = window.__NGB_RUNTIME_CONFIG__
  if (!runtimeConfig || typeof runtimeConfig !== 'object') return null

  return runtimeConfig
}

export function readAppEnv(name: string): string {
  const runtimeConfig = readWindowRuntimeConfig()
  if (runtimeConfig && Object.prototype.hasOwnProperty.call(runtimeConfig, name)) {
    return String(runtimeConfig[name] ?? '').trim()
  }

  return String(import.meta.env[name as keyof ImportMetaEnv] ?? '').trim()
}
