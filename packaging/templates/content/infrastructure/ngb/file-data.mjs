import assert from 'node:assert/strict'
import { constants } from 'node:fs'
import { lstat, open, readdir, realpath } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { distinctPaths, maximumAssetBytes, maximumAssets, portablePath } from './kit-contracts.mjs'

export async function safePath(root, path) {
  portablePath(path)
  const absolute = resolve(root)
  assert.equal(await realpath(absolute), absolute, 'Application root must be a real path, not a symbolic link.')
  let current = absolute
  const parts = path.split('/')
  for (let index = 0; index < parts.length; index += 1) {
    current = join(current, parts[index])
    try {
      const stat = await lstat(current)
      assert.ok(!stat.isSymbolicLink(), `Symbolic links are not supported for managed assets: ${path}`)
      assert.ok(index === parts.length - 1 || stat.isDirectory(), `Asset parent is not a directory: ${path}`)
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
  return current
}

export async function readBytes(root, path, limit = maximumAssetBytes) {
  assert.ok(Number.isSafeInteger(limit) && limit > 0 && limit <= 256 * 1024 * 1024, 'Invalid file read limit.')
  const location = await safePath(root, path)
  let handle
  try {
    handle = await open(location, constants.O_RDONLY | constants.O_NOFOLLOW)
    const before = await handle.stat()
    assert.ok(before.isFile() && before.nlink === 1, `Asset must be a regular file with one link: ${path}`)
    assert.ok(before.size <= limit, `Asset exceeds its size limit: ${path}`)
    const bytes = Buffer.alloc(before.size + 1)
    let offset = 0
    while (offset < bytes.length) {
      const { bytesRead } = await handle.read(bytes, offset, bytes.length - offset, offset)
      if (bytesRead === 0) break
      offset += bytesRead
    }
    const after = await handle.stat()
    assert.ok(offset === before.size && before.mtimeMs === after.mtimeMs && before.size === after.size,
      `Asset changed while being read: ${path}`)
    return bytes.subarray(0, offset)
  } catch (error) {
    if (error.code === 'ENOENT') return null
    throw error
  } finally {
    await handle?.close()
  }
}

export async function readAsset(root, path, limit = maximumAssetBytes) {
  const bytes = await readBytes(root, path, limit)
  if (bytes === null) return null
  const text = bytes.toString('utf8')
  assert.ok(Buffer.from(text).equals(bytes), `Asset is not UTF-8: ${path}`)
  return text
}

export async function assetPaths(root, directory) {
  const paths = []
  let count = 0
  async function visit(path, depth) {
    assert.ok(depth <= 12, 'Asset directory nesting exceeds its limit.')
    const location = await safePath(root, path)
    let entries
    try {
      entries = await readdir(location, { withFileTypes: true })
    } catch (error) {
      if (error.code === 'ENOENT') return
      throw error
    }
    count += entries.length
    assert.ok(count <= maximumAssets, 'Asset directory exceeds its entry limit.')
    for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name, 'en'))) {
      const child = `${path}/${entry.name}`
      assert.ok(!entry.isSymbolicLink(), `Symbolic link in asset directory: ${child}`)
      if (entry.isDirectory()) await visit(child, depth + 1)
      else {
        assert.ok(entry.isFile(), `Unsupported asset kind: ${child}`)
        paths.push(child)
      }
    }
  }
  await visit(directory, 0)
  distinctPaths(paths)
  return paths.sort()
}

