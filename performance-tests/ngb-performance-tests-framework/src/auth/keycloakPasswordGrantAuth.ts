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
    const cached = this.cache.getValidToken();
    if (cached) {
      return cached;
    }

    this.applyInitialJitter();

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

    const response = http.post(this.env.keycloakTokenUrl, formUrlEncode({
      grant_type: 'password',
      client_id: this.env.keycloakClientId,
      client_secret: this.env.keycloakClientSecret,
      username: this.env.username,
      password: this.env.password,
    }), params);

    authDuration.add(response.timings.duration, tags);

    const ok = check(response, {
      'keycloak token status is 200': (res) => res.status === 200,
      'keycloak token response has access token': (res) => {
        const token = readTokenResponse(res).access_token;
        return typeof token === 'string' && token.length > 0;
      },
    }, tags);

    if (!ok) {
      this.cache.clear();
      abortTest(`[ngb-perf] Keycloak password grant failed: ${JSON.stringify(safeErrorSummary(response))}`);
    }

    const tokenResponse = readTokenResponse(response);
    const accessToken = tokenResponse.access_token;
    if (!accessToken) {
      abortTest('[ngb-perf] Keycloak token response did not include an access token.');
    }

    this.cache.set(accessToken, tokenResponse.expires_in ?? 60, this.safetyBufferSeconds);
    return accessToken;
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

function formUrlEncode(values: Record<string, string>): string {
  return Object.entries(values)
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
}

function abortTest(message: string): never {
  exec.test.abort(message);
  fail(message);
}
