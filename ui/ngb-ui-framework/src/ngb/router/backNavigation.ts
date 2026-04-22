import type { RouteLocationNormalizedLoaded, Router } from 'vue-router';

function normalizeQueryValue(value: unknown): string | null {
  if (Array.isArray(value)) return normalizeQueryValue(value[0] ?? null);
  const raw = String(value ?? '').trim();
  return raw.length > 0 ? raw : null;
}

function encodeBase64Url(value: string): string {
  const bytes = new TextEncoder().encode(value);
  let binary = '';
  bytes.forEach((byte) => {
    binary += String.fromCharCode(byte);
  });
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

function decodeBase64Url(value: string): string | null {
  try {
    const padded = value.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((value.length + 3) % 4);
    const binary = atob(padded);
    const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  } catch {
    return null;
  }
}

export function encodeBackTarget(target: string | null | undefined): string | null {
  const normalized = String(target ?? '').trim();
  if (!normalized) return null;
  return encodeBase64Url(normalized);
}

export function decodeBackTarget(value: unknown): string | null {
  const normalized = normalizeQueryValue(value);
  if (!normalized) return null;
  return decodeBase64Url(normalized);
}

function parseRouteTarget(target: string | null | undefined): URL | null {
  const normalized = String(target ?? '').trim();
  if (!normalized) return null;

  try {
    // Provide a synthetic absolute base only so the URL constructor can parse app-relative routes
    // like "/documents/...". This value is never used for network requests and should not represent
    // a real environment or host.
    return new URL(normalized, 'http://ngb.local');
  } catch {
    return null;
  }
}

export function resolveBackTargetFromPath(target: string | null | undefined): string | null {
  const parsed = parseRouteTarget(target);
  if (!parsed) return null;
  return decodeBackTarget(parsed.searchParams.get('back'));
}

export function routeTargetMatches(
  candidate: string | null | undefined,
  expected: string | null | undefined,
): boolean {
  const parsedCandidate = parseRouteTarget(candidate);
  const parsedExpected = parseRouteTarget(expected);
  if (!parsedCandidate || !parsedExpected) return false;
  if (parsedCandidate.pathname !== parsedExpected.pathname) return false;

  for (const [key, value] of parsedExpected.searchParams.entries()) {
    if (parsedCandidate.searchParams.get(key) !== value) return false;
  }

  return true;
}

export function buildPathWithQuery(path: string, patch: Record<string, string | null | undefined>): string {
  const [pathname, rawQuery = ''] = path.split('?', 2);
  const query = new URLSearchParams(rawQuery);

  Object.entries(patch).forEach(([key, value]) => {
    const normalized = String(value ?? '').trim();
    if (normalized.length === 0) query.delete(key);
    else query.set(key, normalized);
  });

  const suffix = query.toString();
  return suffix ? `${pathname}?${suffix}` : pathname;
}

export function withBackTarget(path: string, backTarget: string | null | undefined): string {
  const encoded = encodeBackTarget(backTarget);
  return buildPathWithQuery(path, { back: encoded });
}

export function currentRouteBackTarget(route: Pick<RouteLocationNormalizedLoaded, 'fullPath'>): string {
  return String(route.fullPath ?? '').trim() || '/';
}

export function resolveBackTarget(route: Pick<RouteLocationNormalizedLoaded, 'query'>): string | null {
  return decodeBackTarget(route.query?.back);
}

export async function navigateBack(
  router: Router,
  route: Pick<RouteLocationNormalizedLoaded, 'query'>,
  fallback?: string | null,
): Promise<void> {
  const explicit = resolveBackTarget(route);
  if (explicit) {
    await router.replace(explicit);
    return;
  }

  const normalizedFallback = String(fallback ?? '').trim();
  if (normalizedFallback.length > 0 && typeof window !== 'undefined' && window.history.length <= 1) {
    await router.replace(normalizedFallback);
    return;
  }

  router.back();
}
