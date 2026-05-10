import type { AccessTokenProvider } from '../core/httpClient.ts';

export class StaticAccessTokenProvider implements AccessTokenProvider {
  private readonly accessToken: string;

  constructor(accessToken: string) {
    const trimmed = accessToken.trim();
    if (!trimmed) {
      throw new Error('Static access token must not be empty.');
    }

    this.accessToken = trimmed;
  }

  getAccessToken(): string {
    return this.accessToken;
  }
}
