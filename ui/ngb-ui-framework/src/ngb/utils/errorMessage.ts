export function toErrorMessage(error: unknown, fallback = 'Request failed.'): string {
  if (error instanceof Error) {
    const message = error.message.trim()
    if (message) return message
  }

  if (error && typeof error === 'object' && 'message' in error) {
    const message = String((error as { message?: unknown }).message ?? '').trim()
    if (message) return message
  }

  const raw = String(error ?? '').trim()
  return raw || fallback
}
