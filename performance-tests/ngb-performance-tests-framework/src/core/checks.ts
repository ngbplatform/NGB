import { check } from 'k6';

export interface JsonResponseLike {
  readonly status: number;
  json(): unknown;
}

export function statusIs(response: JsonResponseLike, expected: number, tags?: Record<string, string>): boolean {
  return check(response, {
    [`status is ${expected}`]: (res) => res.status === expected,
  }, tags);
}

export function statusIsOneOf(response: JsonResponseLike, expected: readonly number[], tags?: Record<string, string>): boolean {
  const label = expected.join('/');
  return check(response, {
    [`status is one of ${label}`]: (res) => expected.includes(res.status),
  }, tags);
}

export function jsonHas(response: JsonResponseLike, path: string, tags?: Record<string, string>): boolean {
  return check(response, {
    [`json has ${path}`]: (res) => getPathValue(readJson(res), path) !== undefined,
  }, tags);
}

export function jsonArrayNotEmpty(response: JsonResponseLike, path = '', tags?: Record<string, string>): boolean {
  return check(response, {
    [path ? `json array ${path} is not empty` : 'json array is not empty']: (res) => {
      const value = path ? getPathValue(readJson(res), path) : readJson(res);
      return Array.isArray(value) && value.length > 0;
    },
  }, tags);
}

export function operationSucceeded(
  response: JsonResponseLike,
  expected: readonly number[] = [200, 201, 202, 204],
  tags?: Record<string, string>,
): boolean {
  return statusIsOneOf(response, expected, tags);
}

function readJson(response: JsonResponseLike): unknown {
  try {
    return response.json();
  } catch {
    return undefined;
  }
}

function getPathValue(source: unknown, path: string): unknown {
  if (!path) {
    return source;
  }

  const segments = path.split('.').filter((segment) => segment.length > 0);
  let current = source;

  for (const segment of segments) {
    if (current == null) {
      return undefined;
    }

    if (Array.isArray(current)) {
      const index = Number.parseInt(segment, 10);
      current = Number.isInteger(index) ? current[index] : undefined;
      continue;
    }

    if (typeof current === 'object') {
      current = (current as Record<string, unknown>)[segment];
      continue;
    }

    return undefined;
  }

  return current;
}
