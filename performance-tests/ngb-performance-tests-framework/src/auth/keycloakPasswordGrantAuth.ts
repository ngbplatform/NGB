import { check, fail } from 'k6';
import exec from 'k6/execution';
import http, { type Params } from 'k6/http';
import { sleep } from 'k6';

import { readAuthInitialJitterSeconds, type NgbPerfEnv } from '../core/env.ts';
import { safeErrorSummary } from '../core/errors.ts';
import { authDuration } from '../core/metrics.ts';
import { buildTags } from '../core/requestTags.ts';
import { randomInt } from '../core/random.ts';
import { TokenCache } from './tokenCache.ts';

interface KeycloakTokenResponse {
  readonly access_token?: string;
  readonly expires_in?: number;
}

export interface AccessTokenGrant {
  readonly accessToken: string;
  readonly expiresAtUnixMs: number;
}

export class KeycloakPasswordGrantAuth {
  private readonly env: NgbPerfEnv;
  private readonly cache = new TokenCache();
  private readonly safetyBufferSeconds: number;
  private firstTokenRequestAttempted = false;

  constructor(env: NgbPerfEnv, safetyBufferSeconds = 30) {
    this.env = env;
    this.safetyBufferSeconds = safetyBufferSeconds;
  }

  getAccessToken(): string {
    return this.getAccessTokenGrant().accessToken;
  }

  getAccessTokenGrant(): AccessTokenGrant {
    const cached = this.cache.getValidTokenDetails();
    if (cached) {
      return cached;
    }

    this.applyInitialJitter();
    return this.requestAccessTokenWithRetry();
  }

  private requestAccessTokenWithRetry(): AccessTokenGrant {
    const tags = buildTags({
      app: 'ngb',
      vertical: this.env.vertical,
      area: 'auth',
      operation: 'auth.keycloak.password_grant',
    });
    const params: Params = {
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/x-www-form-urlencoded',
      },
      tags,
      timeout: '30s',
    };
    const body = formUrlEncode({
      grant_type: 'password',
      client_id: this.env.keycloakClientId,
      client_secret: this.env.keycloakClientSecret,
      username: this.env.username,
      password: this.env.password,
    });
    let lastResponse: ReturnType<typeof http.post> | null = null;
    let lastTokenResponse: KeycloakTokenResponse = {};

    for (let attempt = 1; attempt <= this.env.authTokenMaxAttempts; attempt += 1) {
      const response = http.post(this.env.keycloakTokenUrl, body, params);
      const tokenResponse = readTokenResponse(response);
      lastResponse = response;
      lastTokenResponse = tokenResponse;
      authDuration.add(response.timings.duration, tags);

      if (response.status === 200 && tokenResponse.access_token) {
        checkTokenResponse(response, tags);
        return this.cache.set(tokenResponse.access_token, tokenResponse.expires_in ?? 60, this.safetyBufferSeconds);
      }

      if (attempt < this.env.authTokenMaxAttempts) {
        console.warn(
          `[ngb-perf] Keycloak password grant attempt ${attempt}/${this.env.authTokenMaxAttempts} failed: ${JSON.stringify(safeErrorSummary(response))}`,
        );
        this.sleepBeforeRetry(attempt);
      }
    }

    if (!lastResponse) {
      abortTest('[ngb-perf] Keycloak password grant was not attempted.');
    }

    const ok = checkTokenResponse(lastResponse, tags);

    if (!ok) {
      this.cache.clear();
      abortTest(`[ngb-perf] Keycloak password grant failed: ${JSON.stringify(safeErrorSummary(lastResponse))}`);
    }

    const accessToken = lastTokenResponse.access_token;
    if (!accessToken) {
      abortTest('[ngb-perf] Keycloak token response did not include an access token.');
    }

    return this.cache.set(accessToken, lastTokenResponse.expires_in ?? 60, this.safetyBufferSeconds);
  }

  private applyInitialJitter(): void {
    if (this.firstTokenRequestAttempted) {
      return;
    }

    this.firstTokenRequestAttempted = true;
    const jitterSeconds = readAuthInitialJitterSeconds();
    if (jitterSeconds <= 0) {
      return;
    }

    const jitterMillis = randomInt(0, Math.round(jitterSeconds * 1000));
    if (jitterMillis > 0) {
      sleep(jitterMillis / 1000);
    }
  }

  private sleepBeforeRetry(failedAttempt: number): void {
    const maxDelayMillis = Math.round(this.env.authTokenRetryBackoffSeconds * 1_000 * (2 ** Math.max(0, failedAttempt - 1)));
    if (maxDelayMillis <= 0) {
      return;
    }

    sleep(randomInt(0, maxDelayMillis) / 1000);
  }
}

function readTokenResponse(response: { json(): unknown }): KeycloakTokenResponse {
  try {
    const value = response.json();
    return typeof value === 'object' && value !== null
      ? value as KeycloakTokenResponse
      : {};
  } catch {
    return {};
  }
}

function checkTokenResponse(response: ReturnType<typeof http.post>, tags: Record<string, string>): boolean {
  return check(response, {
    'keycloak token status is 200': (res) => res.status === 200,
    'keycloak token response has access token': (res) => {
      const token = readTokenResponse(res).access_token;
      return typeof token === 'string' && token.length > 0;
    },
  }, tags);
}

function formUrlEncode(values: Record<string, string>): string {
  return Object.entries(values)
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
}

function abortTest(message: string): never {
  exec.test.abort(message);
  fail(message);
}
