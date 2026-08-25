import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h, nextTick } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  searchCatalog: vi.fn(),
  buildTarget: vi.fn(),
  routerPush: vi.fn(),
  focus: vi.fn(),
  scrollIntoView: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ name: 'lease-edit', params: { id: 'lease-1' } }),
  useRouter: () => ({ push: mocks.routerPush }),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildLookupFieldTargetUrl: mocks.buildTarget,
  isReferenceValue: (value: unknown) => typeof value === 'object' && value !== null && 'id' in value,
  useLookupStore: () => ({ searchCatalog: mocks.searchCatalog }),
  useValidationFocus: () => ({ focus: mocks.focus }),
  NgbIcon: defineComponent({
    name: 'NgbIcon',
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  }),
  NgbLookup: defineComponent({
    name: 'NgbLookup',
    props: {
      modelValue: { type: Object, default: null },
      items: { type: Array, default: () => [] },
      readonly: { type: Boolean, default: false },
      showOpen: { type: Boolean, default: false },
      showClear: { type: Boolean, default: false },
    },
    emits: ['query', 'update:modelValue', 'open'],
    setup(props) {
      return () => h('div', {
        'data-testid': 'lookup',
        'data-item-count': String(props.items.length),
        'data-value': (props.modelValue as { label?: string } | null)?.label ?? '',
        'data-readonly': String(props.readonly),
        'data-open': String(props.showOpen),
        'data-clear': String(props.showClear),
      })
    },
  }),
  NgbSelect: defineComponent({
    name: 'NgbSelect',
    props: { modelValue: { type: String, required: true }, disabled: { type: Boolean, default: false } },
    emits: ['update:modelValue'],
    setup(props) {
      return () => h('div', { 'data-testid': 'role-select', 'data-disabled': String(props.disabled) }, props.modelValue)
    },
  }),
}))

import LeaseTenantsGrid from '../../../../src/components/lease/LeaseTenantsGrid.vue'

type Row = {
  party_id: { id: string; display?: string | null } | string | null | Record<string, unknown>
  role: string
  is_primary: boolean
  ordinal: number
}

function rows(): Row[] {
  return [
    { party_id: { id: 'party-1', display: 'Tenant One' }, role: 'PrimaryTenant', is_primary: true, ordinal: 8 },
    { party_id: 'party-2', role: 'PrimaryTenant', is_primary: false, ordinal: 9 },
    { party_id: { unexpected: true }, role: 'Occupant', is_primary: false, ordinal: 10 },
  ]
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.searchCatalog.mockResolvedValue([])
  mocks.buildTarget.mockResolvedValue(null)
  mocks.routerPush.mockResolvedValue(undefined)
  mocks.focus.mockReturnValue(false)
  HTMLElement.prototype.scrollIntoView = mocks.scrollIntoView
})

describe('LeaseTenantsGrid', () => {
  it('normalizes row values, renders meaningful validation, and focuses preferred, row, summary, and empty errors', async () => {
    const wrapper = mount(LeaseTenantsGrid, {
      props: {
        modelValue: rows() as never,
        errors: {
          summary: ['Tenant validation failed', ' '],
          rowErrors: {
            0: { party_id: ['Party is required', ' '], role: [], ordinal: ['Ordinal error'] },
            1: { role: ['Role conflict'], is_primary: ['Primary conflict'] },
            2: { other: 'not-an-array' } as never,
          },
        },
      },
    })

    expect(wrapper.text()).toContain('Tenant validation failed')
    expect(wrapper.text()).toContain('Party is required')
    expect(wrapper.text()).toContain('Ordinal error')
    expect(wrapper.text()).toContain('Primary conflict')
    const lookups = wrapper.findAllComponents({ name: 'NgbLookup' })
    expect(lookups[0]!.attributes('data-value')).toBe('Tenant One')
    expect(lookups[1]!.attributes('data-value')).toBe('party-2')
    expect(lookups[2]!.attributes('data-value')).toBe('')

    mocks.focus.mockReturnValueOnce(true)
    expect((wrapper.vm as any).focusFirstError({ focusTarget: { rowIndex: 2, field: 'role' } })).toBe(true)
    expect(mocks.focus).toHaveBeenCalledWith('2:role')

    mocks.focus.mockReset()
    mocks.focus.mockReturnValueOnce(false).mockReturnValueOnce(true)
    expect((wrapper.vm as any).focusFirstError({
      focusTarget: { rowIndex: 9, field: 'party_id' },
      rowErrors: {
        invalid: { party_id: ['Ignored'] },
        2: { party_id: ['Earlier error'] },
        4: { role: ['Role error'] },
      },
    })).toBe(true)
    expect(mocks.focus).toHaveBeenNthCalledWith(2, '2:party_id')

    mocks.focus.mockReset()
    mocks.focus.mockReturnValue(false)
    expect((wrapper.vm as any).focusFirstError()).toBe(true)
    expect((wrapper.vm as any).focusFirstError({ summary: [] })).toBe(true)
    expect((wrapper.vm as any).focusFirstError({ summary: ['Summary only'], rowErrors: {} })).toBe(true)
    expect(mocks.scrollIntoView).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' })
    expect((wrapper.vm as any).focusFirstError({ summary: [], rowErrors: {} })).toBe(true)

    const withoutErrors = mount(LeaseTenantsGrid, { props: { modelValue: [] } })
    expect((withoutErrors.vm as any).focusFirstError()).toBe(false)
    expect((withoutErrors.vm as any).focusFirstError({ summary: [], rowErrors: {} })).toBe(false)
    expect((withoutErrors.vm as any).focusRowField(1, 'party_id')).toBe(false)
    withoutErrors.unmount()

    const nullableValues = mount(LeaseTenantsGrid, {
      props: {
        modelValue: [
          { party_id: null, role: 'PrimaryTenant', is_primary: true, ordinal: 1 },
          { party_id: { id: 'party-fallback', display: '' }, role: 'CoTenant', is_primary: false, ordinal: 2 },
        ],
      },
    })
    const nullableLookups = nullableValues.findAllComponents({ name: 'NgbLookup' })
    expect(nullableLookups[0]!.attributes('data-value')).toBe('')
    expect(nullableLookups[1]!.attributes('data-value')).toBe('party-fallback')
    nullableLookups[0]!.vm.$emit('open')
    await flushPromises()
    nullableValues.unmount()
    wrapper.unmount()
  })

  it('searches parties, maps lookup results, updates party and role values, and opens valid targets', async () => {
    mocks.searchCatalog
      .mockResolvedValueOnce(undefined)
      .mockResolvedValueOnce([
        { id: 'party-a', label: 'Tenant A', meta: null },
        { id: 'party-b', label: 'Tenant B', meta: 'Active' },
      ])
    mocks.buildTarget.mockResolvedValueOnce(null).mockResolvedValueOnce('/catalogs/pm.party/party-1')
    const wrapper = mount(LeaseTenantsGrid, { props: { modelValue: rows() as never } })
    const lookups = wrapper.findAllComponents({ name: 'NgbLookup' })

    lookups[0]!.vm.$emit('query', '   ')
    lookups[0]!.vm.$emit('query', null)
    await nextTick()
    expect(lookups[0]!.attributes('data-item-count')).toBe('0')
    lookups[0]!.vm.$emit('query', ' tenant ')
    await flushPromises()
    expect(mocks.searchCatalog).toHaveBeenCalledWith('pm.party', 'tenant', { filters: { is_tenant: 'true' } })
    expect(lookups[0]!.attributes('data-item-count')).toBe('0')
    lookups[1]!.vm.$emit('query', 'second')
    await flushPromises()
    expect(lookups[1]!.attributes('data-item-count')).toBe('2')

    lookups[0]!.vm.$emit('update:modelValue', null)
    lookups[1]!.vm.$emit('update:modelValue', { id: 'party-new', label: 'Tenant New' })
    const selects = wrapper.findAllComponents({ name: 'NgbSelect' })
    selects[1]!.vm.$emit('update:modelValue', 'PrimaryTenant')
    selects[2]!.vm.$emit('update:modelValue', 'Guarantor')
    await nextTick()

    const emitted = wrapper.emitted('update:modelValue')!
    expect(emitted[0]![0]).toEqual([
      expect.objectContaining({ party_id: null, ordinal: 1 }),
      expect.objectContaining({ ordinal: 2 }),
      expect.objectContaining({ ordinal: 3 }),
    ])
    expect(emitted[1]![0]).toEqual(expect.arrayContaining([expect.objectContaining({ party_id: { id: 'party-new', display: 'Tenant New' } })]))
    expect(emitted[2]![0]).toEqual([
      expect.objectContaining({ role: 'CoTenant', is_primary: false }),
      expect.objectContaining({ role: 'PrimaryTenant', is_primary: true }),
      expect.objectContaining({ is_primary: false }),
    ])
    expect(emitted[3]![0]).toEqual(expect.arrayContaining([expect.objectContaining({ role: 'Guarantor' })]))

    lookups[0]!.vm.$emit('open')
    lookups[1]!.vm.$emit('open')
    await flushPromises()
    expect(mocks.routerPush).toHaveBeenCalledOnce()
    expect(mocks.routerPush).toHaveBeenCalledWith('/catalogs/pm.party/party-1')
    wrapper.unmount()
  })

  it('enforces exactly one primary while adding, toggling, and deleting every row shape', async () => {
    const empty = mount(LeaseTenantsGrid, { props: { modelValue: [] } })
    await empty.get('button').trigger('click')
    expect(empty.emitted('update:modelValue')![0]![0]).toEqual([
      { party_id: null, role: 'PrimaryTenant', is_primary: true, ordinal: 1 },
    ])
    empty.unmount()

    const wrapper = mount(LeaseTenantsGrid, { props: { modelValue: rows() as never } })
    await wrapper.findAll('input[type="checkbox"]')[0]!.setValue(false)
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.findAll('input[type="checkbox"]')[1]!.trigger('change')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.findAll('input[type="checkbox"]')[2]!.setValue(true)
    expect(wrapper.emitted('update:modelValue')![0]![0]).toEqual([
      expect.objectContaining({ role: 'CoTenant', is_primary: false }),
      expect.objectContaining({ role: 'CoTenant', is_primary: false }),
      expect.objectContaining({ role: 'PrimaryTenant', is_primary: true }),
    ])

    await wrapper.get('button:not([title])').trigger('click')
    expect(wrapper.emitted('update:modelValue')!.at(-1)![0]).toEqual([
      expect.objectContaining({ is_primary: true, ordinal: 1 }),
      expect.objectContaining({ is_primary: false, ordinal: 2 }),
      expect.objectContaining({ is_primary: false, ordinal: 3 }),
      { party_id: null, role: 'CoTenant', is_primary: false, ordinal: 4 },
    ])

    await wrapper.findAll('button[title="Delete"]')[0]!.trigger('click')
    expect(wrapper.emitted('update:modelValue')!.at(-1)![0]).toEqual([
      expect.objectContaining({ role: 'PrimaryTenant', is_primary: true, ordinal: 1 }),
      expect.objectContaining({ ordinal: 2 }),
    ])
    await wrapper.findAll('button[title="Delete"]')[1]!.trigger('click')
    expect(wrapper.emitted('update:modelValue')!.at(-1)![0]).toEqual([
      expect.objectContaining({ role: 'PrimaryTenant', is_primary: true, ordinal: 1 }),
      expect.objectContaining({ ordinal: 2 }),
    ])
    wrapper.unmount()

    const single = mount(LeaseTenantsGrid, { props: { modelValue: [rows()[0]!] as never } })
    await single.get('button[title="Delete"]').trigger('click')
    expect(single.emitted('update:modelValue')![0]![0]).toEqual([
      { party_id: null, role: 'PrimaryTenant', is_primary: true, ordinal: 1 },
    ])
    single.unmount()
  })

  it('reorders rows from drag state and transfer data, ignores invalid drops, and blocks readonly mutations', async () => {
    const dispatchDrag = async (element: Element, type: string, dataTransfer: unknown) => {
      const event = new Event(type, { bubbles: true, cancelable: true })
      Object.defineProperty(event, 'dataTransfer', { value: dataTransfer })
      element.dispatchEvent(event)
      await nextTick()
    }
    const wrapper = mount(LeaseTenantsGrid, { props: { modelValue: rows() as never } })
    const tableRows = wrapper.findAll('tbody tr')
    const dataTransfer = { setData: vi.fn(), setDragImage: vi.fn(), getData: vi.fn(() => '0') }
    await dispatchDrag(tableRows[0]!.element, 'dragstart', dataTransfer)
    expect(dataTransfer.setData).toHaveBeenCalledWith('text/plain', '0')
    expect(dataTransfer.setDragImage).toHaveBeenCalled()
    await dispatchDrag(tableRows[2]!.element, 'dragover', dataTransfer)
    await dispatchDrag(tableRows[2]!.element, 'drop', dataTransfer)
    expect(wrapper.emitted('update:modelValue')![0]![0]).toEqual([
      expect.objectContaining({ party_id: 'party-2', ordinal: 1 }),
      expect.objectContaining({ party_id: { unexpected: true }, ordinal: 2 }),
      expect.objectContaining({ party_id: { id: 'party-1', display: 'Tenant One' }, ordinal: 3 }),
    ])

    await dispatchDrag(tableRows[1]!.element, 'drop', { getData: () => 'bad' })
    await dispatchDrag(tableRows[1]!.element, 'drop', { getData: () => '1' })
    await dispatchDrag(tableRows[1]!.element, 'drop', null)
    expect(wrapper.emitted('update:modelValue')).toHaveLength(1)

    const throwingTransfer = {
      setData: () => { throw new Error('blocked') },
      setDragImage: vi.fn(),
      getData: () => '2',
    }
    await dispatchDrag(tableRows[2]!.element, 'dragstart', throwingTransfer)
    await dispatchDrag(tableRows[0]!.element, 'drop', throwingTransfer)
    expect(wrapper.emitted('update:modelValue')).toHaveLength(2)
    wrapper.unmount()

    const readonly = mount(LeaseTenantsGrid, { props: { modelValue: rows() as never, readonly: true } })
    const readonlyRow = readonly.get('tbody tr')
    await dispatchDrag(readonlyRow.element, 'dragstart', dataTransfer)
    await dispatchDrag(readonlyRow.element, 'dragover', dataTransfer)
    await dispatchDrag(readonlyRow.element, 'drop', dataTransfer)
    await readonly.get('button[title="Delete"]').trigger('click')
    expect(readonly.emitted('update:modelValue')).toBeUndefined()
    expect(readonly.find('button:not([title])').exists()).toBe(false)
    expect(readonly.get('button[title="Delete"]').attributes('disabled')).toBeDefined()
    expect(readonly.get('[data-testid="lookup"]').attributes()).toMatchObject({
      'data-readonly': 'true',
      'data-clear': 'false',
    })
    readonly.unmount()
  })
})
