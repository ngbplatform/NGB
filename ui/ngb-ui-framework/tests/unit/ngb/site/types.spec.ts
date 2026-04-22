import { describe, expect, it } from 'vitest'

import type { SiteNavNode, SiteQuickLink, SiteSettingsSection } from '../../../../src/ngb/site/types'

describe('site types', () => {
  it('supports navigation, quick link, and settings section contracts', () => {
    const navNode: SiteNavNode = {
      id: 'properties',
      label: 'Properties',
      route: '/catalogs/pm.property',
      icon: 'building-2',
      children: [
        {
          id: 'residential',
          label: 'Residential',
          route: '/catalogs/pm.property?segment=residential',
        },
      ],
    }
    const quickLink: SiteQuickLink = {
      id: 'open-ar',
      title: 'Open receivables',
      subtitle: 'A/R aging and collections',
      route: '/reports/ar.open',
    }
    const settings: SiteSettingsSection = {
      label: 'Platform',
      items: [
        {
          label: 'Users',
          description: 'Manage platform access',
          route: '/settings/users',
          icon: 'users',
        },
      ],
    }

    expect(navNode.children?.[0]?.label).toBe('Residential')
    expect(quickLink.route).toBe('/reports/ar.open')
    expect(settings.items[0]?.icon).toBe('users')
  })
})
