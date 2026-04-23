import { defineConfig } from 'vitepress'

const rawBasePath = process.env.VITEPRESS_BASE_PATH?.trim()
const base = rawBasePath
  ? rawBasePath.endsWith('/')
    ? rawBasePath
    : `${rawBasePath}/`
  : '/'
const docsHostname = (process.env.VITEPRESS_HOSTNAME?.trim() || 'https://docs.ngbplatform.com').replace(/\/$/, '')
const asset = (relativePath: string) => `${base}${relativePath}`
const page = (text: string, link: string) => ({ text, link })
const section = (text: string, items: Array<ReturnType<typeof page>>, collapsed = false) => ({ text, items, collapsed })
const sitemapHostname = base === '/' ? `${docsHostname}/` : `${docsHostname}${base}`
const gtmContainerId = 'GTM-WFZ2RMM7'
const gtmHeadSnippet = `(function(w,d,s,l,i){w[l]=w[l]||[];w[l].push({'gtm.start':
new Date().getTime(),event:'gtm.js'});var f=d.getElementsByTagName(s)[0],
j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src=
'https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);
})(window,document,'script','dataLayer','${gtmContainerId}');`


export default defineConfig({
  base,
  title: 'Docs',
  description: 'Documentation for NGB Platform.',
  lang: 'en-US',
  lastUpdated: true,
  sitemap: {
    hostname: sitemapHostname
  },
  head: [
    ['link', { rel: 'icon', href: asset('favicon.svg'), type: 'image/svg+xml' }],
    ['meta', { name: 'theme-color', content: '#0B3C5D' }],
    ['script', {}, gtmHeadSnippet]
  ],
  themeConfig: {
    logo: asset('images/ngb-logo-color.svg'),
    nav: [
      { text: 'Overview', link: '/' },
      { text: 'Start Here', link: '/start-here/overview' },
      { text: 'Architecture', link: '/architecture/overview' },
      { text: 'Platform Modules', link: '/platform/runtime' },
      { text: 'Guides', link: '/guides/developer-workflows' },
      { text: 'Reference', link: '/reference/documentation-map' },
      { text: 'Website', link: 'https://ngbplatform.com' }
    ],
    outline: [2, 3],
    search: { provider: 'local' },
    sidebar: {
      '/start-here/': [
        section('Start Here', [
          page('Platform Overview', '/start-here/overview'),
          page('Reading Path', '/start-here/reading-path'),
          page('Run Locally', '/start-here/run-locally'),
          page('Manual Local Runbook', '/start-here/manual-local-runbook'),
          page('Repository Structure', '/start-here/repository-structure'),
          page('Host Composition', '/start-here/host-composition')
        ])
      ],
      '/architecture/': [
        section('Core Architecture', [
          page('Architecture Overview', '/architecture/overview'),
          page('Layering and Dependencies', '/architecture/layering-and-dependencies'),
          page('Definitions and Metadata', '/architecture/definitions-and-metadata'),
          page('Runtime Request Flow', '/architecture/runtime-request-flow'),
          page('HTTP to Runtime to PostgreSQL Execution Map', '/architecture/http-runtime-postgres-execution-map'),
          page('Host Composition and DI Map', '/architecture/host-composition-and-di-map')
        ]),
        section('Business Model Concepts', [
          page('Catalogs', '/architecture/catalogs'),
          page('Documents', '/architecture/documents'),
          page('Document Flow', '/architecture/document-flow'),
          page('Accounting Effects', '/architecture/accounting-effects'),
          page('Reporting: Canonical and Composable', '/architecture/reporting'),
          page('Accounting and Posting', '/architecture/accounting-posting'),
          page('Closing Period', '/architecture/closing-period'),
          page('Operational Registers', '/architecture/operational-registers'),
          page('Reference Registers', '/architecture/reference-registers'),
          page('Derive', '/architecture/derive'),
          page('Append-only and Storno', '/architecture/append-only-and-storno'),
          page('Idempotency and Concurrency', '/architecture/idempotency-and-concurrency')
        ], true)
      ],
      '/platform/': [
        section('Platform Modules', [
          page('Core and Tools', '/platform/core-and-tools'),
          page('Metadata', '/platform/metadata'),
          page('Definitions', '/platform/definitions'),
          page('Runtime', '/platform/runtime'),
          page('API', '/platform/api'),
          page('Persistence', '/platform/persistence'),
          page('PostgreSQL', '/platform/postgresql'),
          page('Accounting and Registers', '/platform/accounting-and-registers'),
          page('Background Jobs', '/platform/background-jobs'),
          page('Migrator', '/platform/migrator'),
          page('Watchdog', '/platform/watchdog'),
          page('Security and SSO', '/platform/security-and-sso'),
          page('Audit Log', '/platform/audit-log')
        ]),
        section('Source Maps', [
          page('Runtime Source Map', '/platform/runtime-source-map'),
          page('Runtime Execution Map', '/platform/runtime-execution-map'),
          page('Reporting Execution Map', '/platform/reporting-execution-map'),
          page('PostgreSQL Source Map', '/platform/postgresql-source-map'),
          page('API Source Map', '/platform/api-source-map'),
          page('Metadata Source Map', '/platform/metadata-source-map'),
          page('Definitions Source Map', '/platform/definitions-source-map'),
          page('Accounting Source Map', '/platform/accounting-source-map'),
          page('Operational Registers Source Map', '/platform/operational-registers-source-map'),
          page('Reference Registers Source Map', '/platform/reference-registers-source-map'),
          page('Ops Hosts and Bootstraps Source Map', '/platform/ops-hosts-and-bootstraps-source-map'),
          page('Source-Anchored Class Maps', '/platform/source-anchored-class-maps')
        ], true),
        section('Deep Dives', [
          page('Topic Chapters Index', '/platform/topic-chapters-index'),
          page('Accounting and Posting Deep Dive', '/platform/accounting-posting-deep-dive'),
          page('Operational Registers Deep Dive', '/platform/operational-registers-deep-dive'),
          page('Reference Registers Deep Dive', '/platform/reference-registers-deep-dive'),
          page('Documents, Flow, Effects, Derive', '/platform/documents-flow-effects-deep-dive'),
          page('Closing Period Deep Dive', '/platform/closing-period-deep-dive'),
          page('Audit Log Deep Dive', '/platform/audit-log-deep-dive'),
          page('Background Jobs Deep Dive', '/platform/background-jobs-deep-dive'),
          page('Migrator Deep Dive', '/platform/migrator-deep-dive'),
          page('Watchdog Deep Dive', '/platform/watchdog-deep-dive')
        ], true),
        section('Dense Chapters', [
          page('Runtime Execution Core Dense Source Map', '/platform/runtime-execution-core-dense-source-map'),
          page('Document Subsystem Dense Source Map', '/platform/document-subsystem-dense-source-map'),
          page('Reporting Subsystem Dense Source Map', '/platform/reporting-subsystem-dense-source-map'),
          page('Ops and Tooling Subsystem Dense Source Map', '/platform/ops-tooling-dense-source-map'),
          page('Definitions and Metadata Boundary Dense Source Map', '/platform/definitions-metadata-boundary-dense-source-map'),
          page('Document + Definitions Integration Dense Source Map', '/platform/document-definitions-integration-dense-source-map'),
          page('Reporting + Definitions Integration Dense Source Map', '/platform/reporting-definitions-integration-dense-source-map'),
          page('Document + Reporting Cross-Cutting Integration', '/platform/document-reporting-cross-cutting-dense-source-map')
        ], true),
        section('Collaborator Maps', [
          page('Runtime Execution Core Collaborators Map', '/platform/runtime-execution-core-collaborators-map'),
          page('Document Subsystem Collaborators Map', '/platform/document-subsystem-collaborators-map'),
          page('Reporting Class Collaborators Map', '/platform/reporting-class-collaborators-map'),
          page('Ops and Tooling Class Collaborators Map', '/platform/ops-tooling-class-collaborators-map'),
          page('Definitions and Metadata Collaborators Map', '/platform/definitions-metadata-collaborators-map'),
          page('Document + Definitions Integration Collaborators Map', '/platform/document-definitions-integration-collaborators-map'),
          page('Reporting + Definitions Integration Collaborators Map', '/platform/reporting-definitions-integration-collaborators-map'),
          page('Document + Reporting Cross-Cutting Collaborators', '/platform/document-reporting-cross-cutting-collaborators-map')
        ], true),
        section('Supporting Models', [
          page('API runtime PostgreSQL integration', '/platform/api-runtime-postgres-integration'),
          page('Platform document persistence model', '/platform/platform-document-persistence-model')
        ], true)
      ],
      '/guides/': [
        section('Core Workflows', [
          page('Developer Workflows', '/guides/developer-workflows'),
          page('Platform Extension Points', '/guides/platform-extension-points'),
          page('Add a Document with Accounting and Registers', '/guides/add-document-with-accounting-and-registers'),
          page('Add a Canonical Report', '/guides/add-canonical-report-workflow'),
          page('Add a Composable Report', '/guides/add-composable-report-workflow')
        ]),
        section('Scenario Guides', [
          page('Guide: Business Partners Catalog', '/guides/catalogs/business-partners'),
          page('Guide: Sales Invoice', '/guides/documents/sales-invoice'),
          page('Guide: Item Price Update', '/guides/documents/item-price-update'),
          page('Guide: Inventory and Receivables Operational Registers', '/guides/operational-registers/inventory-and-receivables'),
          page('Guide: Item Pricing Reference Register', '/guides/reference-registers/item-pricing'),
          page('Guide: Canonical and Composable Reports', '/guides/reports/canonical-and-composable')
        ], true)
      ],
      '/reference/': [
        section('Site Guide', [
          page('Documentation Map', '/reference/documentation-map'),
          page('Documentation Consolidation Guide', '/reference/docs-consolidation-guide')
        ]),
        section('Operational Reference', [
          page('Platform Projects', '/reference/platform-projects'),
          page('Configuration Reference', '/reference/configuration-reference'),
          page('Background Job Catalog', '/reference/background-job-catalog'),
          page('Migrator CLI', '/reference/migrator-cli'),
          page('Platform API Surface', '/reference/platform-api-surface'),
          page('Layering Rules', '/reference/layering-rules'),
          page('Database Naming Quick Reference', '/reference/database-naming'),
          page('Database Naming and DDL Patterns', '/reference/database-naming-and-ddl-patterns')
        ]),
        section('Verified Anchor Sets', [
          page('Definitions/Metadata Boundary Verified Anchors', '/reference/definitions-metadata-boundary-verified-anchors'),
          page('Document Subsystem Verified Anchors', '/reference/document-subsystem-verified-anchors'),
          page('Reporting Subsystem Verified Anchors', '/reference/reporting-subsystem-verified-anchors'),
          page('Runtime Execution Core Verified Anchors', '/reference/runtime-execution-core-verified-anchors'),
          page('Ops and Tooling Verified Anchors', '/reference/ops-tooling-verified-anchors'),
          page('Document + Definitions Integration Verified Anchors', '/reference/document-definitions-integration-verified-anchors'),
          page('Reporting + Definitions Integration Verified Anchors', '/reference/reporting-definitions-integration-verified-anchors'),
          page('Document + Reporting Cross-Cutting Verified Anchors', '/reference/document-reporting-cross-cutting-verified-anchors')
        ], true)
      ],
      '/': [
        section('Documentation', [
          page('Documentation Home', '/'),
          page('Platform Overview', '/start-here/overview'),
          page('Reading Path', '/start-here/reading-path'),
          page('Architecture Overview', '/architecture/overview'),
          page('Topic Chapters Index', '/platform/topic-chapters-index'),
          page('Configuration Reference', '/reference/configuration-reference'),
          page('Documentation Map', '/reference/documentation-map')
        ])
      ]
    },
    footer: {
      message: 'Released under the Apache License 2.0.',
      copyright: 'Copyright © NGB Platform'
    },
    docFooter: {
      prev: 'Previous page',
      next: 'Next page'
    }
  }
})
