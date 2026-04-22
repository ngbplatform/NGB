import type { RouteLocationRaw, Router } from 'vue-router'
import type { ToastApi } from '../primitives/toast'
import { toErrorMessage } from '../utils/errorMessage'

type ToastsLike = Pick<ToastApi, 'push'>

export function buildAbsoluteAppUrl(router: Router, to: RouteLocationRaw): string {
  const href = router.resolve(to).href
  if (typeof window !== 'undefined' && window.location?.origin) {
    return new URL(href, window.location.origin).toString()
  }
  return href
}

export async function copyAppLink(
  router: Router,
  toasts: ToastsLike,
  to: RouteLocationRaw,
  opts?: { title?: string; message?: string },
): Promise<boolean> {
  try {
    const url = buildAbsoluteAppUrl(router, to)
    if (!navigator?.clipboard?.writeText) throw new Error('Clipboard is not available.')
    await navigator.clipboard.writeText(url)
    toasts.push({
      title: opts?.title ?? 'Link copied',
      message: opts?.message ?? 'Shareable link copied to clipboard.',
      tone: 'neutral',
    })
    return true
  } catch (cause) {
    toasts.push({
      title: 'Could not copy link',
      message: toErrorMessage(cause, 'Clipboard is not available.'),
      tone: 'danger',
    })
    return false
  }
}
