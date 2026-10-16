export type FeatureState = {
  code: string
  displayName: string
  group: string
  enabled: boolean
}

export const NGB_FEATURES = {
  attachments: 'Attachments',
  notes: 'Notes',
  mcp: 'Mcp',
} as const
