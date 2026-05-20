import type { AccessTokenProvider } from '../core/httpClient.ts';
import { randomInt } from '../core/random.ts';
import type { AccessTokenGrant, KeycloakPasswordGrantAuth } from './keycloakPasswordGrantAuth.ts';

export class SeededAccessTokenProvider implements AccessTokenProvider {
  private seed: SeededAccessToken | null;
  private readonly refreshProvider: KeycloakPasswordGrantAuth;
  private softRefreshFailureLogged = false;

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
    const seed = this.seed;
    if (!seed) {
      return this.refreshProvider.getAccessToken();
    }

    const nowUnixMs = Date.now();
    if (seed.refreshAtUnixMs > nowUnixMs) {
      return seed.accessToken;
    }

    const refreshed = this.refreshProvider.tryGetAccessTokenGrant();
    if (refreshed) {
      this.seed = null;
      return refreshed.accessToken;
    }

    if (seed.expiresAtUnixMs > nowUnixMs + SEED_HARD_EXPIRY_SAFETY_MS) {
      seed.refreshAtUnixMs = nextSoftRefreshAttemptAt(nowUnixMs, seed.expiresAtUnixMs);
      this.logSoftRefreshFailureOnce(seed.refreshAtUnixMs - nowUnixMs);
      return seed.accessToken;
    }

    this.seed = null;
    return this.refreshProvider.getAccessToken();
  }

  invalidateAccessToken(accessToken: string): void {
    if (this.seed?.accessToken === accessToken) {
      this.seed = null;
    }

    this.refreshProvider.invalidateAccessToken(accessToken);
  }

  private logSoftRefreshFailureOnce(nextAttemptDelayMs: number): void {
    if (this.softRefreshFailureLogged) {
      return;
    }

    this.softRefreshFailureLogged = true;
    console.warn(
      `[ngb-perf] Seed access token soft-refresh failed; keeping still-valid token and retrying refresh in ${Math.max(0, nextAttemptDelayMs / 1000).toFixed(1)}s.`,
    );
  }
}

interface SeededAccessToken extends AccessTokenGrant {
  refreshAtUnixMs: number;
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

const SEED_HARD_EXPIRY_SAFETY_MS = 5_000;
const SOFT_REFRESH_RETRY_MIN_MS = 5_000;
const SOFT_REFRESH_RETRY_MAX_MS = 30_000;

function nextSoftRefreshAttemptAt(nowUnixMs: number, expiresAtUnixMs: number): number {
  const latestAttemptUnixMs = expiresAtUnixMs - SEED_HARD_EXPIRY_SAFETY_MS;
  if (latestAttemptUnixMs <= nowUnixMs) {
    return nowUnixMs;
  }

  const retryDelayMs = randomInt(SOFT_REFRESH_RETRY_MIN_MS, SOFT_REFRESH_RETRY_MAX_MS);
  return Math.min(nowUnixMs + retryDelayMs, latestAttemptUnixMs);
}
