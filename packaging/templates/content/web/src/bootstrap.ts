import { createApp } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import {
  configureNgbCommandPalette,
  configureNgbEditor,
  configureNgbNavigation,
  configureNgbWorkCenter,
  createDefaultNgbWorkCenterConfig,
  executeDocumentAction,
  getDocumentById,
  getDocumentEditorState,
  getDocumentEffects,
  getDocumentGraph,
  getEntityAuditLog,
  useAccessStore,
  useAuthStore,
  useMainMenuStore,
} from '@ngbplatform/ui'
import '@ngbplatform/ui/styles'
import { searchApplication } from './search'

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
  configureNgbEditor({
    print: { openInNewTab: true },
    loadDocumentById: getDocumentById,
    loadDocumentEffects: getDocumentEffects,
    loadDocumentGraph: getDocumentGraph,
    loadEntityAuditLog: getEntityAuditLog,
    documentActions: { loadEditorState: getDocumentEditorState, execute: executeDocumentAction },
  })

  const [{ default: App }, { router }] = await Promise.all([import('./App.vue'), import('./router')])
  configureNgbCommandPalette({
    router,
    recentStorageKey: 'application:recent-commands',
    getMenuGroups: () => useMainMenuStore().groups,
    searchRemote: searchApplication,
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
