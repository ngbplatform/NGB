import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'

import type {
  WorkCenterRealtimeClient,
  WorkCenterRealtimeHandlers,
} from './config'

export function createSignalRWorkCenterClient(args: {
  baseUrl: string
  getAccessToken: () => Promise<string | null>
}): WorkCenterRealtimeClient {
  let connection: HubConnection | null = null
  let startPromise: Promise<void> | null = null

  async function start(handlers: WorkCenterRealtimeHandlers): Promise<void> {
    if (connection) return startPromise ?? undefined

    const next = new HubConnectionBuilder()
      .withUrl(new URL('/hubs/work-center', args.baseUrl).toString(), {
        accessTokenFactory: async () => await args.getAccessToken() ?? '',
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      })
      .configureLogging(LogLevel.Error)
      .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
      .build()

    next.on('workCenterChanged', handlers.changed)
    next.onreconnected(handlers.reconnected)
    next.onclose(() => {
      if (connection === next) connection = null
      handlers.disconnected()
    })
    connection = next
    startPromise = next.start().catch(async (cause) => {
      if (connection === next) connection = null
      await next.stop().catch(() => undefined)
      throw cause
    }).finally(() => {
      startPromise = null
    })
    await startPromise
  }

  async function stop(): Promise<void> {
    const active = connection
    connection = null
    if (active) await active.stop().catch(() => undefined)
  }

  return { start, stop }
}
