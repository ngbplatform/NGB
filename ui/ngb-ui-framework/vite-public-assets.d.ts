import type { Plugin } from 'vite'

export interface NgbPublicAssetsOptions {
  faviconFileName?: string
  silentCheckSsoFileName?: string
}

export declare function ngbUiFrameworkPublicAssetsPlugin(options?: NgbPublicAssetsOptions): Plugin
