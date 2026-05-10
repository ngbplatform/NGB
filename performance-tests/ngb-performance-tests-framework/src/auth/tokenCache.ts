export interface CachedAccessToken {
  readonly accessToken: string;
  readonly expiresAtUnixMs: number;
}

export class TokenCache {
  private token: CachedAccessToken | null = null;

  getValidToken(nowUnixMs = Date.now()): string | null {
    if (!this.token || this.token.expiresAtUnixMs <= nowUnixMs) {
      return null;
    }

    return this.token.accessToken;
  }

  set(accessToken: string, expiresInSeconds: number, safetyBufferSeconds: number, nowUnixMs = Date.now()): void {
    const safeExpiresIn = Math.max(1, expiresInSeconds - safetyBufferSeconds);
    this.token = {
      accessToken,
      expiresAtUnixMs: nowUnixMs + safeExpiresIn * 1000,
    };
  }

  clear(): void {
    this.token = null;
  }
}
