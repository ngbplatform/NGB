import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, reactive } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import NgbEntityFormFieldsBlock from '../../../../src/ngb/metadata/NgbEntityFormFieldsBlock.vue'
import type { FieldMetadata } from '../../../../src/ngb/metadata/types'

function field(key: string, label: string, dataType: string, extra: Partial<FieldMetadata> = {}): FieldMetadata {
  return { key, label, dataType, uiControl: 1, isRequired: false, isReadOnly: false, ...extra }
}

const rows = [{ fields: [
  field('amount', 'Amount', 'Money'),
  field('notes', 'Notes', 'String', { uiControl: 2 }),
  field('active', 'Active', 'Boolean'),
  field('status', 'Status', 'String', { options: [{ label: 'Open', value: 'open' }] }),
  field('effective', 'Effective date', 'Date'),
  field('owner_id', 'Owner', 'Guid', { lookup: { kind: 'catalog', catalogType: 'example.owner' } }),
  field('original', 'Original', 'String', { isReadOnly: true }),
] }]

test('visible metadata labels name their own native controls in concurrent editors', async () => {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: {} }] })
  const Editor = defineComponent({
    setup() {
      const model = reactive({ display: '', active: false, original: { id: 'saved', display: 'Saved record' } })
      return () => h(NgbEntityFormFieldsBlock, {
        rows, model, entityTypeCode: 'example.form', displayField: field('display', 'Name', 'String'),
      })
    },
  })
  const view = await render(defineComponent({
    setup: () => () => h('main', [h(Editor), h(Editor)]),
  }), { global: { plugins: [router] } })

  for (const [name, role] of [
    ['Name', 'textbox'], ['Amount', 'spinbutton'], ['Notes', 'textbox'], ['Active', 'checkbox'],
    ['Status', 'button'], ['Effective date', 'button'], ['Owner', 'combobox'], ['Original', 'textbox'],
  ] as const) {
    const controls = view.getByRole(role, { name, exact: true }).elements()
    expect(controls, name).toHaveLength(2)
    expect(controls[0].id).not.toBe(controls[1].id)
    for (const control of controls) {
      const label = document.querySelector<HTMLLabelElement>(`label[for="${CSS.escape(control.id)}"]`)
      expect(label?.textContent).toBe(name)
      expect(label?.control).toBe(control)
    }
  }

  const second = view.getByRole('textbox', { name: 'Name', exact: true }).nth(1)
  expect(view.getByRole('button', { name: 'Show options', exact: true }).elements()).toHaveLength(2)
  await view.getByText('Name', { exact: true }).nth(1).click()
  await expect.element(second).toHaveFocus()
  await view.getByText('Active', { exact: true }).nth(1).click()
  await expect.element(view.getByRole('checkbox', { name: 'Active' }).nth(1)).toBeChecked()
  await expect.element(view.getByRole('checkbox', { name: 'Active' }).first()).not.toBeChecked()
})
