export interface CachedAccessToken {
  readonly accessToken: string;
  readonly expiresAtUnixMs: number;
}

export class TokenCache {
  private token: CachedAccessToken | null = null;

  getValidTokenDetails(nowUnixMs = Date.now()): CachedAccessToken | null {
    if (!this.token || this.token.expiresAtUnixMs <= nowUnixMs) {
      return null;
    }

    return this.token;
  }

  getValidToken(nowUnixMs = Date.now()): string | null {
    return this.getValidTokenDetails(nowUnixMs)?.accessToken ?? null;
  }

  set(
    accessToken: string,
    expiresInSeconds: number,
    safetyBufferSeconds: number,
    nowUnixMs = Date.now(),
  ): CachedAccessToken {
    const safeExpiresIn = Math.max(1, expiresInSeconds - safetyBufferSeconds);
    const token = {
      accessToken,
      expiresAtUnixMs: nowUnixMs + safeExpiresIn * 1000,
    };
    this.token = token;
    return token;
  }

  clear(): void {
    this.token = null;
  }

  clearIfMatches(accessToken: string): void {
    if (this.token?.accessToken === accessToken) {
      this.token = null;
    }
  }
}
