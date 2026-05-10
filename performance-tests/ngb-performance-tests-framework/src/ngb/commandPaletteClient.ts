import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';

export interface CommandPaletteSearchRequest {
  readonly query: string;
  readonly limit?: number;
  readonly context?: Record<string, unknown> | null;
}

export class CommandPaletteClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  search(request: CommandPaletteSearchRequest): NgbHttpResponse {
    return this.http.post('/api/search/command-palette', {
      query: request.query,
      limit: request.limit ?? 8,
      context: request.context ?? null,
    }, {
      tags: {
        vertical: this.env.vertical,
        area: 'command-palette',
        operation: 'platform.command_palette.search',
      },
    });
  }
}
