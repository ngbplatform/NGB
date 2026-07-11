function readPort(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(String(value ?? '').trim(), 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export const TRADE_WEB_DEV_HOST = process.env.TRADE_WEB_DEV_HOST?.trim() || 'localhost'
export const TRADE_WEB_DEV_PORT = readPort(process.env.TRADE_WEB_DEV_PORT, 5174)
export const TRADE_WEB_DEV_BASE_URL = `http://${TRADE_WEB_DEV_HOST}:${TRADE_WEB_DEV_PORT}`
