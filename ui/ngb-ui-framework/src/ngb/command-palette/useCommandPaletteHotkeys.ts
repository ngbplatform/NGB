import { onBeforeUnmount, onMounted } from 'vue'
import { useCommandPaletteStore } from './store'

export function useCommandPaletteHotkeys(): void {
  const store = useCommandPaletteStore()

  function onKeyDown(event: KeyboardEvent): void {
    const isCommandShortcut = (event.metaKey || event.ctrlKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'k'
    if (!isCommandShortcut) return
    if (!store.isOpen && isEditableTarget(event.target)) return

    event.preventDefault()
    store.open()
  }

  onMounted(() => window.addEventListener('keydown', onKeyDown))
  onBeforeUnmount(() => window.removeEventListener('keydown', onKeyDown))
}

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true

  const closest = target.closest('input, textarea, select, [contenteditable="true"], [role="textbox"]')
  return closest instanceof HTMLElement
}

