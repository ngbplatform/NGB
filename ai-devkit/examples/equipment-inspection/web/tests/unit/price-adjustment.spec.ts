import { defineComponent, h, ref } from 'vue'
import { render } from 'vitest-browser-vue'
import { expect, it, vi } from 'vitest'
import type { Adjustment } from '../../src/PriceAdjustment.vue'

vi.hoisted(() => vi.stubGlobal('__NGB_RUNTIME_CONFIG__', {
  VITE_KEYCLOAK_URL: 'http://localhost:18081',
  VITE_KEYCLOAK_REALM: 'component-tests',
  VITE_KEYCLOAK_CLIENT_ID: 'component-tests',
}))

it('accepts a reason before the discount, preserves it across price changes and protects readonly states', async () => {
  const { default: Editor } = await import('../../src/PriceAdjustment.vue')
  const value = ref<Adjustment>({ priceMinor: 1000, reason: '' })
  const readonly = ref(false)
  const canAdjust = ref(true)
  const screen = render(defineComponent({
    setup: () => () => h(Editor, {
      modelValue: value.value,
      quotedPriceMinor: 1000,
      readonly: readonly.value,
      canAdjust: canAdjust.value,
      'onUpdate:modelValue': next => {
        value.value = next
      },
    }),
  }))
  const price = screen.getByRole('spinbutton', { name: 'Agreed price (cents)' })
  const reason = screen.getByRole('textbox', { name: 'Discount reason' })
  await reason.fill('Customer loyalty')
  await price.fill('800')
  expect(value.value).toEqual({ priceMinor: 800, reason: 'Customer loyalty' })
  await expect.element(screen.getByRole('alert')).not.toBeInTheDocument()
  await reason.fill('')
  await expect.element(screen.getByRole('alert')).toHaveTextContent('Enter a discount reason')
  await reason.fill('Approved adjustment')
  await price.fill('900')
  expect(value.value.reason).toBe('Approved adjustment')
  for (const invalid of ['-1', '0.5', '9007199254740992', '']) {
    await price.fill(invalid)
    expect(value.value.priceMinor).toBe(900)
  }
  await price.fill('0')
  expect(value.value.priceMinor).toBe(0)
  readonly.value = true
  await expect.element(reason).toHaveAttribute('readonly')
  await expect.element(price).toHaveAttribute('readonly')
  readonly.value = false
  canAdjust.value = false
  await expect.element(reason).toHaveAttribute('readonly')
  await expect.element(screen.getByText('Price adjustments require an editable draft and adjustment permission.')).toBeVisible()
  // An input event can still arrive after permission changes; it must not mutate the draft.
  const input = reason.element() as HTMLInputElement
  input.value = 'Late edit'
  input.dispatchEvent(new Event('input', { bubbles: true }))
  expect(value.value.reason).toBe('Approved adjustment')
})
