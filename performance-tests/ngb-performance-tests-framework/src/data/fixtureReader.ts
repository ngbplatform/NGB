export function readJsonFixture<T>(relativePath: string): T {
  const content = open(relativePath);
  const parsed = JSON.parse(content);
  return parsed as T;
}
