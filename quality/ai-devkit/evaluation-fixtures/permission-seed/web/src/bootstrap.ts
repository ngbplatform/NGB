import { createApp } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import {
  configureNgbCommandPalette,
  configureNgbEditor,
  configureNgbLookup,
  configureNgbMetadata,
  configureNgbReporting,
  configureNgbWorkCenter,
  createDefaultNgbLookupConfig,
  createDefaultNgbReportingConfig,
  createDefaultNgbWorkCenterConfig,
  executeDocumentAction,
  getCatalogTypeMetadata,
  getDocumentById,
  getDocumentEditorState,
  getDocumentEffects,
  getDocumentGraph,
  getDocumentTypeMetadata,
  getEntityAuditLog,
  normalizeCatalogTypeMetadata,
  useAccessStore,
  useAuthStore,
} from '@ngbplatform/ui'
import { configureNgbNavigation } from '@ngbplatform/ui/navigation'
import '@ngbplatform/ui/styles'
import { metadataFormBehavior } from './metadata'

async function bootstrap(): Promise<void> {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  await auth.initialize()

  if (!auth.authenticated) {
    await auth.login(window.location.pathname + window.location.search)
    return
  }

  await useAccessStore(pinia).load()

  configureNgbNavigation()
  configureNgbWorkCenter(createDefaultNgbWorkCenterConfig())
  configureNgbLookup(createDefaultNgbLookupConfig())
  configureNgbReporting(createDefaultNgbReportingConfig())
  configureNgbMetadata({
    loadCatalogTypeMetadata: async code => {
      const metadata = normalizeCatalogTypeMetadata(await getCatalogTypeMetadata(code))
      return { ...metadata, capabilities: { ...metadata.capabilities, canCreate: true, canEdit: true } }
    },
    loadDocumentTypeMetadata: getDocumentTypeMetadata,
    formBehavior: metadataFormBehavior,
  })
  configureNgbEditor({
    loadDocumentById: getDocumentById,
    loadDocumentEffects: getDocumentEffects,
    loadDocumentGraph: getDocumentGraph,
    loadEntityAuditLog: getEntityAuditLog,
    documentActions: { loadEditorState: getDocumentEditorState, execute: executeDocumentAction },
  })

  const [{ default: App }, { router }] = await Promise.all([import('./App.vue'), import('./router')])
  configureNgbCommandPalette({
    router,
    recentStorageKey: 'equipment-inspection:recent-commands',
    getMenuGroups: () => [{ label: 'Equipment inspection', items: [
      { code: 'equipment', label: 'Equipment', route: '/catalogs/example.equipment' },
      { code: 'inspections', label: 'Inspections', route: '/documents/example.inspection' },
      { code: 'equipment-usage', label: 'Equipment usage', route: '/documents/example.equipment-usage' },
      { code: 'equipment-condition', label: 'Equipment assessments', route: '/documents/example.equipment-condition' },
      { code: 'equipment-service', label: 'Equipment service', route: '/documents/example.equipment-service' },
      { code: 'inspection-summary', label: 'Inspection summary', route: '/reports/example.inspection-summary' },
      { code: 'period-closing', label: 'Period closing', route: '/admin/accounting/period-closing' },
    ] }],
  })
  const app = createApp(App).use(pinia).use(router)
  await router.isReady()
  app.mount('#app')
}

export function startApplication(): Promise<void> {
  return bootstrap().catch((error: unknown) => {
    const root = document.getElementById('app')
    if (root) root.textContent = error instanceof Error ? error.message : 'Application startup failed.'
  })
}
