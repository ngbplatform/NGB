import type { CommandPaletteStoreConfig } from './types'

let commandPaletteConfig: CommandPaletteStoreConfig | null = null

export function configureNgbCommandPalette(config: CommandPaletteStoreConfig): void {
  commandPaletteConfig = config
}

export function getConfiguredNgbCommandPalette(): CommandPaletteStoreConfig {
  if (!commandPaletteConfig) {
    throw new Error('NGB command palette is not configured. Call configureNgbCommandPalette(...) during app bootstrap.')
  }

  return commandPaletteConfig
}

