import { readdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { loadConfigFromFile } from 'vite'

const scriptsDirectory = path.dirname(fileURLToPath(import.meta.url))
const docsRoot = path.resolve(scriptsDirectory, '..')
const sidebarConfigPath = path.join(docsRoot, '.vitepress', 'config.ts')
const ignoredDirectories = new Set(['.vitepress', 'node_modules'])

async function collectMarkdownFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = []

  for (const entry of entries) {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) {
      continue
    }

    const entryPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      files.push(...await collectMarkdownFiles(entryPath))
    } else if (entry.isFile() && entry.name.endsWith('.md')) {
      files.push(entryPath)
    }
  }

  return files
}

function routeForMarkdownFile(filePath) {
  const relativePath = path.relative(docsRoot, filePath).split(path.sep).join('/')
  return relativePath === 'index.md' ? '/' : `/${relativePath.slice(0, -'.md'.length)}`
}

function markdownFileForRoute(route) {
  return route === '/'
    ? path.join(docsRoot, 'index.md')
    : path.join(docsRoot, `${route.slice(1)}.md`)
}

// Load the real TypeScript config through Vite. Parsing every page(...) call with a regex can
// accidentally count a route that exists only in top navigation as a sidebar route.
const loadedConfig = await loadConfigFromFile(
  { command: 'build', mode: 'production' },
  sidebarConfigPath
)
const sidebar = loadedConfig?.config?.themeConfig?.sidebar

if (!sidebar || typeof sidebar !== 'object' || Array.isArray(sidebar)) {
  throw new Error('VitePress themeConfig.sidebar must be a route-keyed object.')
}

function collectSidebarRoutes(items, sidebarKey, configuredRoutes, duplicateRoutes) {
  if (!Array.isArray(items)) {
    throw new Error(`VitePress sidebar entry ${sidebarKey} must be an array.`)
  }

  const routesInThisSidebar = new Set()

  function visit(entry) {
    if (!entry || typeof entry !== 'object') {
      throw new Error(`VitePress sidebar entry ${sidebarKey} contains a non-object item.`)
    }

    if (typeof entry.link === 'string' && entry.link.startsWith('/')) {
      const route = entry.link === '/' ? '/' : entry.link.replace(/\/$/, '')
      configuredRoutes.add(route)
      if (routesInThisSidebar.has(route)) {
        duplicateRoutes.push(`${sidebarKey}: ${route}`)
      } else {
        routesInThisSidebar.add(route)
      }
    }

    if (entry.items !== undefined) {
      if (!Array.isArray(entry.items)) {
        throw new Error(`VitePress sidebar item in ${sidebarKey} has non-array items.`)
      }
      for (const child of entry.items) visit(child)
    }
  }

  for (const entry of items) visit(entry)
}

const configuredRoutes = new Set()
const duplicateSidebarRoutes = []
for (const [sidebarKey, items] of Object.entries(sidebar)) {
  collectSidebarRoutes(items, sidebarKey, configuredRoutes, duplicateSidebarRoutes)
}

const markdownFiles = await collectMarkdownFiles(docsRoot)
const markdownRoutes = new Set(markdownFiles.map(routeForMarkdownFile))
const missingFromSidebar = [...markdownRoutes]
  .filter((route) => !configuredRoutes.has(route))
  .sort()
const brokenSidebarRoutes = [...configuredRoutes]
  .filter((route) => !markdownRoutes.has(route))
  .sort()

if (missingFromSidebar.length > 0 || brokenSidebarRoutes.length > 0 || duplicateSidebarRoutes.length > 0) {
  if (missingFromSidebar.length > 0) {
    console.error('Markdown pages missing from the VitePress sidebar:')
    for (const route of missingFromSidebar) {
      console.error(`  - ${route} (${path.relative(docsRoot, markdownFileForRoute(route))})`)
    }
  }

  if (brokenSidebarRoutes.length > 0) {
    console.error('VitePress sidebar routes without matching Markdown pages:')
    for (const route of brokenSidebarRoutes) {
      console.error(`  - ${route}`)
    }
  }

  if (duplicateSidebarRoutes.length > 0) {
    console.error('Duplicate routes within the same VitePress sidebar:')
    for (const route of duplicateSidebarRoutes) {
      console.error(`  - ${route}`)
    }
  }

  process.exitCode = 1
} else {
  console.log(`Verified navigation for ${markdownRoutes.size} documentation pages.`)
}
