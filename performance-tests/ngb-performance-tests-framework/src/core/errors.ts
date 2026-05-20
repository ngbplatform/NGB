export interface NgbHttpLikeResponse {
  readonly status: number;
  readonly error?: string;
  readonly body?: string | ArrayBuffer | null;
}

export interface SafeHttpErrorSummary {
  readonly status: number;
  readonly error?: string;
  readonly bodyLength: number;
  readonly bodyPreview?: string;
}

export function safeErrorSummary(response: NgbHttpLikeResponse): SafeHttpErrorSummary {
  const body = typeof response.body === 'string' ? response.body : '';
  const summary: SafeHttpErrorSummary = {
    status: response.status,
    bodyLength: body.length,
  };

  if (response.error) {
    return body.length > 0
      ? { ...summary, error: response.error, bodyPreview: sanitizePreview(body.slice(0, 180)) }
      : { ...summary, error: response.error };
  }

  return body.length > 0
    ? { ...summary, bodyPreview: sanitizePreview(body.slice(0, 180)) }
    : summary;
}

function sanitizePreview(value: string): string {
  return value
    .replace(/"access_token"\s*:\s*"[^"]+"/gi, '"access_token":"***"')
    .replace(/"refresh_token"\s*:\s*"[^"]+"/gi, '"refresh_token":"***"')
    .replace(/"password"\s*:\s*"[^"]+"/gi, '"password":"***"')
    .replace(/"client_secret"\s*:\s*"[^"]+"/gi, '"client_secret":"***"');
}
