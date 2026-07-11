function readPort(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(String(value ?? '').trim(), 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export const CRM_WEB_DEV_HOST = process.env.CRM_WEB_DEV_HOST?.trim() || 'localhost'
export const CRM_WEB_DEV_PORT = readPort(process.env.CRM_WEB_DEV_PORT, 5176)
export const CRM_WEB_DEV_BASE_URL = `http://${CRM_WEB_DEV_HOST}:${CRM_WEB_DEV_PORT}`
