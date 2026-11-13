import { execFileSync } from 'node:child_process'

export function sourceFiles(repository) {
  const list = options => execFileSync('git', ['ls-files', ...options, '-z'], {
    cwd: repository, encoding: 'utf8',
  }).split('\0').filter(Boolean)
  // Git's index still lists tracked files deleted from the working tree until
  // their removal is staged. Hash and copy the same working-tree contents in
  // either state, while preserving read errors for files Git reports as present.
  const deleted = new Set(list(['--deleted']))
  const paths = list(['--cached', '--others', '--exclude-standard'])
  return [...new Set(paths)]
    .filter(path => !deleted.has(path) && path !== 'AGENTS.md' && !path.endsWith('.DS_Store'))
    .sort()
}
