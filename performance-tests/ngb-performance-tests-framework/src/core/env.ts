const secretNamePattern = /(PASSWORD|SECRET|TOKEN|KEY)$/i;

export interface NgbPerfEnv {
  readonly baseUrl: string;
  readonly apiBaseUrl: string;
  readonly vertical: string;
  readonly keycloakTokenUrl: string;
  readonly keycloakClientId: string;
  readonly keycloakClientSecret: string;
  readonly username: string;
  readonly password: string;
  readonly tenantCode?: string;
  readonly companyCode?: string;
  readonly environmentName: string;
  readonly summaryExportPath?: string;
  readonly enableWrites: boolean;
  readonly hostAliases: Record<string, string>;
  readonly insecureSkipTlsVerify: boolean;
  readonly authInitialJitterSeconds: number;
}

export function readNgbPerfEnv(overrides: Record<string, string | undefined> = __ENV): NgbPerfEnv {
  const required = [
    'NGB_BASE_URL',
    'NGB_API_BASE_URL',
    'NGB_VERTICAL',
    'KEYCLOAK_TOKEN_URL',
    'KEYCLOAK_TESTER_CLIENT_ID',
    'KEYCLOAK_TESTER_CLIENT_SECRET',
    'NGB_TEST_USERNAME',
    'NGB_TEST_PASSWORD',
  ];

  const missing = required.filter((name) => !readEnvValue(overrides, name));
  if (missing.length > 0) {
    throw new Error(`Missing required NGB performance environment variables: ${missing.join(', ')}`);
  }

  const tenantCode = optionalEnv(overrides, 'NGB_TEST_TENANT_CODE');
  const companyCode = optionalEnv(overrides, 'NGB_TEST_COMPANY_CODE');
  const summaryExportPath = optionalEnv(overrides, 'NGB_K6_SUMMARY_EXPORT');

  return {
    baseUrl: normalizeUrl(requiredEnv(overrides, 'NGB_BASE_URL')),
    apiBaseUrl: normalizeUrl(requiredEnv(overrides, 'NGB_API_BASE_URL')),
    vertical: requiredEnv(overrides, 'NGB_VERTICAL'),
    keycloakTokenUrl: normalizeUrl(requiredEnv(overrides, 'KEYCLOAK_TOKEN_URL')),
    keycloakClientId: requiredEnv(overrides, 'KEYCLOAK_TESTER_CLIENT_ID'),
    keycloakClientSecret: requiredEnv(overrides, 'KEYCLOAK_TESTER_CLIENT_SECRET'),
    username: requiredEnv(overrides, 'NGB_TEST_USERNAME'),
    password: requiredEnv(overrides, 'NGB_TEST_PASSWORD'),
    environmentName: optionalEnv(overrides, 'NGB_K6_ENV') ?? 'local',
    enableWrites: parseBoolean(optionalEnv(overrides, 'NGB_PERF_ENABLE_WRITES')),
    hostAliases: readK6HostAliases(overrides),
    insecureSkipTlsVerify: readK6InsecureSkipTlsVerify(overrides),
    authInitialJitterSeconds: readAuthInitialJitterSeconds(overrides),
    ...(tenantCode ? { tenantCode } : {}),
    ...(companyCode ? { companyCode } : {}),
    ...(summaryExportPath ? { summaryExportPath } : {}),
  };
}

export function readK6HostAliases(overrides: Record<string, string | undefined> = __ENV): Record<string, string> {
  return parseHostAliases(optionalEnv(overrides, 'NGB_K6_HOST_ALIASES'));
}

export function readK6InsecureSkipTlsVerify(overrides: Record<string, string | undefined> = __ENV): boolean {
  const explicit = optionalEnv(overrides, 'NGB_K6_INSECURE_SKIP_TLS_VERIFY');
  if (explicit) {
    return parseBoolean(explicit);
  }

  return (optionalEnv(overrides, 'NGB_K6_ENV') ?? 'local').toLowerCase() === 'local';
}

export function readAuthInitialJitterSeconds(overrides: Record<string, string | undefined> = __ENV): number {
  return parseNonNegativeNumber(optionalEnv(overrides, 'NGB_AUTH_INITIAL_JITTER_SECONDS'));
}

export function safeEnvNameList(env: NgbPerfEnv): string[] {
  return [
    'NGB_BASE_URL',
    'NGB_API_BASE_URL',
    'NGB_VERTICAL',
    'KEYCLOAK_TOKEN_URL',
    'KEYCLOAK_TESTER_CLIENT_ID',
    'NGB_TEST_USERNAME',
    'NGB_TEST_TENANT_CODE',
    'NGB_TEST_COMPANY_CODE',
    'NGB_K6_SUMMARY_EXPORT',
    'NGB_K6_ENV',
    'NGB_PERF_ENABLE_WRITES',
    'NGB_K6_HOST_ALIASES',
    'NGB_K6_INSECURE_SKIP_TLS_VERIFY',
    'NGB_AUTH_INITIAL_JITTER_SECONDS',
  ].filter((name) => envValueIsPresent(env, name));
}

export function assertSafeToLogEnvName(name: string): void {
  if (secretNamePattern.test(name)) {
    throw new Error(`Refusing to log sensitive environment variable: ${name}`);
  }
}

function envValueIsPresent(env: NgbPerfEnv, name: string): boolean {
  switch (name) {
    case 'NGB_BASE_URL':
      return env.baseUrl.length > 0;
    case 'NGB_API_BASE_URL':
      return env.apiBaseUrl.length > 0;
    case 'NGB_VERTICAL':
      return env.vertical.length > 0;
    case 'KEYCLOAK_TOKEN_URL':
      return env.keycloakTokenUrl.length > 0;
    case 'KEYCLOAK_TESTER_CLIENT_ID':
      return env.keycloakClientId.length > 0;
    case 'NGB_TEST_USERNAME':
      return env.username.length > 0;
    case 'NGB_TEST_TENANT_CODE':
      return !!env.tenantCode;
    case 'NGB_TEST_COMPANY_CODE':
      return !!env.companyCode;
    case 'NGB_K6_SUMMARY_EXPORT':
      return !!env.summaryExportPath;
    case 'NGB_K6_ENV':
      return env.environmentName.length > 0;
    case 'NGB_PERF_ENABLE_WRITES':
      return env.enableWrites;
    case 'NGB_K6_HOST_ALIASES':
      return Object.keys(env.hostAliases).length > 0;
    case 'NGB_K6_INSECURE_SKIP_TLS_VERIFY':
      return env.insecureSkipTlsVerify;
    case 'NGB_AUTH_INITIAL_JITTER_SECONDS':
      return env.authInitialJitterSeconds > 0;
    default:
      return false;
  }
}

function requiredEnv(source: Record<string, string | undefined>, name: string): string {
  const value = readEnvValue(source, name);
  if (!value) {
    throw new Error(`Missing required NGB performance environment variable: ${name}`);
  }

  return value;
}

function optionalEnv(source: Record<string, string | undefined>, name: string): string | undefined {
  return readEnvValue(source, name);
}

function readEnvValue(source: Record<string, string | undefined>, name: string): string | undefined {
  const value = source[name]?.trim();
  return value && value.length > 0 ? value : undefined;
}

function normalizeUrl(value: string): string {
  return value.replace(/\/+$/, '');
}

function parseBoolean(value: string | undefined): boolean {
  if (!value) {
    return false;
  }

  return ['1', 'true', 'yes', 'on'].includes(value.trim().toLowerCase());
}

function parseNonNegativeNumber(value: string | undefined): number {
  if (!value) {
    return 0;
  }

  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0) {
    throw new Error(`Expected a non-negative number but received: ${value}`);
  }

  return parsed;
}

function parseHostAliases(value: string | undefined): Record<string, string> {
  if (value && ['0', 'false', 'none', 'off'].includes(value.trim().toLowerCase())) {
    return {};
  }

  const aliases: Record<string, string> = {};
  if (!value) {
    return aliases;
  }

  for (const rawEntry of value.split(/[;,]/)) {
    const entry = rawEntry.trim();
    if (!entry) {
      continue;
    }

    const separatorIndex = entry.indexOf('=');
    if (separatorIndex < 1) {
      throw new Error(`Invalid NGB_K6_HOST_ALIASES entry: ${entry}. Expected host=ip.`);
    }

    const host = entry.slice(0, separatorIndex).trim();
    const target = entry.slice(separatorIndex + 1).trim();
    if (!host || !target) {
      throw new Error(`Invalid NGB_K6_HOST_ALIASES entry: ${entry}. Expected host=ip.`);
    }

    aliases[host] = target;
  }

  return aliases;
}
