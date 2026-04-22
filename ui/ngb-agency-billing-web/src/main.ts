import { createApp } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import {
  configureNgbCommandPalette,
  configureNgbEditor,
  configureNgbLookup,
  configureNgbMetadata,
  configureNgbReporting,
  createDefaultNgbLookupConfig,
  createDefaultNgbReportingConfig,
  useAuthStore,
} from 'ngb-ui-framework'

import 'ngb-ui-framework/styles'

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
    { createAgencyBillingCommandPaletteConfig },
    { createAgencyBillingMetadataConfig },
    { createAgencyBillingEditorConfig },
  ] = await Promise.all([
    import('./App.vue'),
    import('./router/router'),
    import('./command-palette/config'),
    import('./metadata/framework'),
    import('./editor/framework'),
  ])

  configureNgbLookup(createDefaultNgbLookupConfig())
  configureNgbEditor(createAgencyBillingEditorConfig())
  configureNgbMetadata(createAgencyBillingMetadataConfig())
  configureNgbReporting(createDefaultNgbReportingConfig())
  configureNgbCommandPalette(createAgencyBillingCommandPaletteConfig(router))

  const app = createApp(App)
  app.use(pinia)
  app.use(router)
  await router.isReady()
  app.mount('#app')
}

void bootstrap()
