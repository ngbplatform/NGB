import type { Ref } from 'vue'

type UseValidationFocusOptions = {
  attribute: string
}

function focusableElement(container: HTMLElement | null): HTMLElement | null {
  if (!container) return null
  return (
    container.querySelector<HTMLElement>('input:not([disabled]), textarea:not([disabled]), select:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])') ??
    null
  )
}

export function useValidationFocus(
  rootRef: Ref<HTMLElement | null>,
  options: UseValidationFocusOptions,
) {
  function containerFor(key: string): HTMLElement | null {
    const root = rootRef.value
    if (!root) return null

    const selector = `[${options.attribute}]`
    const nodes = Array.from(root.querySelectorAll<HTMLElement>(selector))
    return nodes.find((node) => node.getAttribute(options.attribute) === key) ?? null
  }

  function scrollTo(key: string): boolean {
    const container = containerFor(key)
    if (!container) return false
    container.scrollIntoView({ block: 'center', behavior: 'smooth' })
    return true
  }

  function focus(key: string): boolean {
    const container = containerFor(key)
    if (!container) return false
    container.scrollIntoView({ block: 'center', behavior: 'smooth' })
    const target = focusableElement(container)
    target?.focus({ preventScroll: true } as FocusOptions)
    return true
  }

  return {
    containerFor,
    scrollTo,
    focus,
  }
}
