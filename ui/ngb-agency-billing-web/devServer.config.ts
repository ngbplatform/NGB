function readPort(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(String(value ?? '').trim(), 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export const AGENCY_BILLING_WEB_DEV_HOST = process.env.AGENCY_BILLING_WEB_DEV_HOST?.trim() || 'localhost'
export const AGENCY_BILLING_WEB_DEV_PORT = readPort(process.env.AGENCY_BILLING_WEB_DEV_PORT, 5175)
export const AGENCY_BILLING_WEB_DEV_BASE_URL = `http://${AGENCY_BILLING_WEB_DEV_HOST}:${AGENCY_BILLING_WEB_DEV_PORT}`
