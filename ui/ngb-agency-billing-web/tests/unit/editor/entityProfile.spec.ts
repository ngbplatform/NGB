import { describe, expect, it } from 'vitest'

import { resolveAgencyBillingEditorEntityProfile } from '../../../src/editor/entityProfile'

describe('agency billing editor entity profile', () => {
  it('builds client display from name only', () => {
    const profile = resolveAgencyBillingEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'ab.client',
    } as never)

    const model = {
      client_code: 'CLI-100',
      name: 'Northwind Studio',
      display: '',
    }

    profile?.syncComputedDisplay?.({ model } as never)

    expect(profile?.computedDisplayMode).toBe('always')
    expect(profile?.computedDisplayWatchFields).toEqual(['name'])
    expect(model.display).toBe('Northwind Studio')
  })

  it('builds affected catalog displays from their name fields only', () => {
    const teamMemberProfile = resolveAgencyBillingEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'ab.team_member',
    } as never)
    const projectProfile = resolveAgencyBillingEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'ab.project',
    } as never)
    const serviceItemProfile = resolveAgencyBillingEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'ab.service_item',
    } as never)
    const paymentTermsProfile = resolveAgencyBillingEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'ab.payment_terms',
    } as never)

    const teamMemberModel = {
      member_code: 'TM-100',
      full_name: 'Ava Stone',
      display: '',
    }
    const projectModel = {
      project_code: 'PRJ-100',
      name: 'Website Refresh',
      display: '',
    }
    const serviceItemModel = {
      code: 'DESIGN',
      name: 'Design',
      display: '',
    }
    const paymentTermsModel = {
      code: 'NET30',
      name: 'Net 30',
      display: '',
    }

    teamMemberProfile?.syncComputedDisplay?.({ model: teamMemberModel } as never)
    projectProfile?.syncComputedDisplay?.({ model: projectModel } as never)
    serviceItemProfile?.syncComputedDisplay?.({ model: serviceItemModel } as never)
    paymentTermsProfile?.syncComputedDisplay?.({ model: paymentTermsModel } as never)

    expect(teamMemberProfile?.computedDisplayWatchFields).toEqual(['full_name'])
    expect(projectProfile?.computedDisplayWatchFields).toEqual(['name'])
    expect(serviceItemProfile?.computedDisplayWatchFields).toEqual(['name'])
    expect(paymentTermsProfile?.computedDisplayWatchFields).toEqual(['name'])
    expect(teamMemberModel.display).toBe('Ava Stone')
    expect(projectModel.display).toBe('Website Refresh')
    expect(serviceItemModel.display).toBe('Design')
    expect(paymentTermsModel.display).toBe('Net 30')
  })

  it('builds rate card display from its key descriptive fields', () => {
    const rateCardProfile = resolveAgencyBillingEditorEntityProfile({
      kind: 'catalog',
      typeCode: 'ab.rate_card',
    } as never)

    const rateCardModel = {
      name: 'Senior Consultant',
      service_title: 'Strategy',
      display: '',
    }

    rateCardProfile?.syncComputedDisplay?.({ model: rateCardModel } as never)

    expect(rateCardModel.display).toBe('Senior Consultant · Strategy')
  })

  it('returns null for unsupported agency billing contexts', () => {
    expect(resolveAgencyBillingEditorEntityProfile({
      kind: 'document',
      typeCode: 'ab.sales_invoice',
    } as never)).toBeNull()
  })
})
