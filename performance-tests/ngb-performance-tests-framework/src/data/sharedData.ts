import { SharedArray } from 'k6/data';

export function sharedJsonArray<T>(name: string, relativePath: string): T[] {
  return new SharedArray(name, () => {
    const content = open(relativePath);
    const parsed = JSON.parse(content);
    if (!Array.isArray(parsed)) {
      throw new Error(`Fixture ${relativePath} must contain a JSON array.`);
    }

    return parsed as T[];
  }) as T[];
}
