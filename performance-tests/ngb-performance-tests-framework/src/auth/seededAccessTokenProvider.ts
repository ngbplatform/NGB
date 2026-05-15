import type { AccessTokenProvider } from '../core/httpClient.ts';
import { randomInt } from '../core/random.ts';
import type { AccessTokenGrant, KeycloakPasswordGrantAuth } from './keycloakPasswordGrantAuth.ts';

export class SeededAccessTokenProvider implements AccessTokenProvider {
  private seed: SeededAccessToken | null;
  private readonly refreshProvider: KeycloakPasswordGrantAuth;

  constructor(seed: AccessTokenGrant, refreshProvider: KeycloakPasswordGrantAuth, seedRefreshJitterSeconds: number) {
    const accessToken = seed.accessToken.trim();
    if (!accessToken) {
      throw new Error('Seeded access token must not be empty.');
    }

    this.seed = {
      accessToken,
      expiresAtUnixMs: seed.expiresAtUnixMs,
      refreshAtUnixMs: calculateSeedRefreshAt(seed.expiresAtUnixMs, seedRefreshJitterSeconds),
    };
    this.refreshProvider = refreshProvider;
  }

  getAccessToken(): string {
    if (this.seed && this.seed.refreshAtUnixMs > Date.now()) {
      return this.seed.accessToken;
    }

    this.seed = null;
    return this.refreshProvider.getAccessToken();
  }
}

interface SeededAccessToken extends AccessTokenGrant {
  readonly refreshAtUnixMs: number;
}

function calculateSeedRefreshAt(expiresAtUnixMs: number, seedRefreshJitterSeconds: number): number {
  const nowUnixMs = Date.now();
  const remainingMs = expiresAtUnixMs - nowUnixMs;
  if (remainingMs <= 1_000) {
    return nowUnixMs;
  }

  const jitterWindowMs = Math.min(Math.round(seedRefreshJitterSeconds * 1_000), remainingMs - 1_000);
  if (jitterWindowMs <= 0) {
    return expiresAtUnixMs;
  }

  return expiresAtUnixMs - randomInt(1, jitterWindowMs);
}
