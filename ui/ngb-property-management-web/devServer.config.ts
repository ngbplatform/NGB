function readPort(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(String(value ?? '').trim(), 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export const PM_WEB_DEV_HOST = process.env.PM_WEB_DEV_HOST?.trim() || 'localhost'
export const PM_WEB_DEV_PORT = readPort(process.env.PM_WEB_DEV_PORT, 5173)
export const PM_WEB_DEV_BASE_URL = `http://${PM_WEB_DEV_HOST}:${PM_WEB_DEV_PORT}`
