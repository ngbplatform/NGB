import { readFileSync } from 'node:fs'

import { describe, expect, it } from 'vitest'

const verticals = [
  {
    name: 'Agency Billing',
    configUrl: new URL('../../../../ngb-agency-billing-web/docker/nginx.conf', import.meta.url),
    upstream: 'ngb_ab_api_upstream',
    service: 'ngb.ab.api',
  },
  {
    name: 'CRM',
    configUrl: new URL('../../../../ngb-crm-web/docker/nginx.conf', import.meta.url),
    upstream: 'ngb_crm_api_upstream',
    service: 'ngb.crm.api',
  },
  {
    name: 'Property Management',
    configUrl: new URL('../../../../ngb-property-management-web/docker/nginx.conf', import.meta.url),
    upstream: 'ngb_pm_api_upstream',
    service: 'ngb.pm.api',
  },
  {
    name: 'Trade',
    configUrl: new URL('../../../../ngb-trade-web/docker/nginx.conf', import.meta.url),
    upstream: 'ngb_trade_api_upstream',
    service: 'ngb.trade.api',
  },
] as const

describe('production web proxy configuration', () => {
  it.each(verticals)('$name re-resolves API containers and proxies authenticated SignalR', ({ configUrl, upstream, service }) => {
    const config = readFileSync(configUrl, 'utf8')
    const upstreamBlock = block(config, `upstream ${upstream}`)
    const apiLocation = block(config, 'location /api/')
    const hubLocation = block(config, 'location /hubs/')

    expect(config).toContain('resolver 127.0.0.11 valid=10s ipv6=off;')
    expect(upstreamBlock).toContain(`server ${service}:443 resolve;`)
    expect(apiLocation).toContain(`proxy_pass https://${upstream};`)
    expect(hubLocation).toContain(`proxy_pass https://${upstream};`)
    expect(hubLocation).toContain('proxy_set_header Upgrade $http_upgrade;')
    expect(hubLocation).toContain('proxy_set_header Connection "upgrade";')
    expect(hubLocation).toContain('access_log off;')
    expect(hubLocation).toContain('error_log /dev/stderr crit;')
  })
})

function block(config: string, declaration: string): string {
  const start = config.indexOf(declaration)
  expect(start, `${declaration} must exist`).toBeGreaterThanOrEqual(0)

  const open = config.indexOf('{', start)
  expect(open, `${declaration} must open a block`).toBeGreaterThan(start)

  const close = config.indexOf('}', open)
  expect(close, `${declaration} must close its block`).toBeGreaterThan(open)
  return config.slice(open + 1, close)
}
