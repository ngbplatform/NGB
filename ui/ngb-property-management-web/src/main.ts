import { createApp } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { configureNgbCommandPalette, configureNgbEditor, configureNgbLookup, configureNgbMetadata, configureNgbNavigation, configureNgbReporting, configureNgbWorkCenter, createDefaultNgbLookupConfig, createDefaultNgbReportingConfig, createDefaultNgbWorkCenterConfig, useAuthStore } from '@ngbplatform/ui'

import '@ngbplatform/ui/styles'

async function bootstrap(): Promise<void> {
  const pinia = createPinia()
  setActivePinia(pinia)

  const auth = useAuthStore(pinia)

  try {
    await auth.initialize()
  } catch {
    // Mount the app below so it can render a friendly retry state.
  }

  if (!auth.authenticated && !auth.error) {
    await auth.login(window.location.pathname + window.location.search)
    return
  }

  const [
    { default: App },
    { router },
    { createPmCommandPaletteConfig },
    { createPmMetadataConfig },
    { createPmEditorConfig },
    { createPmNavigationConfig },
  ] = await Promise.all([
    import('./App.vue'),
    import('./router/router'),
    import('./command-palette/config'),
    import('./metadata/framework'),
    import('./editor/framework'),
    import('./navigation/framework'),
  ])

  configureNgbNavigation(createPmNavigationConfig())
  configureNgbWorkCenter(createDefaultNgbWorkCenterConfig())
  configureNgbLookup(createDefaultNgbLookupConfig())
  configureNgbEditor(createPmEditorConfig())
  configureNgbMetadata(createPmMetadataConfig())
  configureNgbReporting(createDefaultNgbReportingConfig())
  configureNgbCommandPalette(createPmCommandPaletteConfig(router))

  const app = createApp(App)
  app.use(pinia)
  app.use(router)
  await router.isReady()
  app.mount('#app')
}

void bootstrap()
