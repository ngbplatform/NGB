import { createApp } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import {
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
  useAuthStore,
} from '@ngbplatform/ui'
import '@ngbplatform/ui/styles'

async function bootstrap(): Promise<void> {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  await auth.initialize()

  configureNgbNavigation()
  configureNgbWorkCenter(createDefaultNgbWorkCenterConfig())
  configureNgbEditor({
    loadDocumentById: getDocumentById,
    loadDocumentEffects: getDocumentEffects,
    loadDocumentGraph: getDocumentGraph,
    loadEntityAuditLog: getEntityAuditLog,
    documentActions: { loadEditorState: getDocumentEditorState, execute: executeDocumentAction },
  })

  const [{ default: App }, { router }] = await Promise.all([import('./App.vue'), import('./router')])
  createApp(App).use(pinia).use(router).mount('#app')
}

void bootstrap().catch((error: unknown) => {
  const root = document.getElementById('app')
  if (root) root.textContent = error instanceof Error ? error.message : 'Application startup failed.'
})
