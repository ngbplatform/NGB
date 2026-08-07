export type NgbNavigationTarget = {
  code: string
  parameters: Record<string, string | null>
}

export type NgbNavigationContext = {
  resourceKind?: string | null
  resourceCode?: string | null
  entityId?: string | null
}

export type NgbNavigationRoutes = {
  workCenter: string
  workCenterPreferences: string
}

export type NgbNavigationConfig = {
  routes?: Partial<NgbNavigationRoutes>
  resolveTarget?: (
    target: NgbNavigationTarget,
    context: NgbNavigationContext,
  ) => string | null
}
