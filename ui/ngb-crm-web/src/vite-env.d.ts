/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string
  readonly VITE_BACKGROUND_JOB_URL?: string
  readonly VITE_KEYCLOAK_CLIENT_ID: string
  readonly VITE_KEYCLOAK_ON_LOAD?: 'login-required' | 'check-sso'
  readonly VITE_KEYCLOAK_POST_LOGOUT_REDIRECT_URL?: string
  readonly VITE_KEYCLOAK_REALM: string
  readonly VITE_KEYCLOAK_REDIRECT_URL?: string
  readonly VITE_KEYCLOAK_ROLE_ADMIN?: string
  readonly VITE_KEYCLOAK_SILENT_CHECK_SSO_ENABLED?: string
  readonly VITE_KEYCLOAK_SILENT_CHECK_SSO_REDIRECT_URI?: string
  readonly VITE_KEYCLOAK_URL: string
  readonly VITE_WATCHDOG_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<Record<string, never>, Record<string, never>, any>
  export default component
}
