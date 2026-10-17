# @ngbplatform/ui

Reusable Vue UI building blocks for NGB Platform vertical applications.

## Install

Replace `PLATFORM_VERSION` with the exact published NGB version used by your backend:

```bash
npm install --save-exact @ngbplatform/ui@PLATFORM_VERSION
```

Applications must provide the Vue runtime peers:

```bash
npm install vue vue-router pinia keycloak-js
```

## Usage

```ts
import { NgbSiteShell } from '@ngbplatform/ui'
import '@ngbplatform/ui/styles'
```

For Vite-hosted applications, publish NGB-owned public assets through the package plugin:

```ts
import { ngbUiFrameworkPublicAssetsPlugin } from '@ngbplatform/ui/vite-public-assets'

export default defineConfig({
  plugins: [vue(), ngbUiFrameworkPublicAssetsPlugin()],
  optimizeDeps: {
    exclude: ['@ngbplatform/ui'],
  },
})
```

The package ships TypeScript modules and Vue SFCs. Exclude it from Vite dependency
pre-bundling so application configuration and components use the same module
instances. Apply this exclusion to separate Vitest browser configurations as well,
and do not add the package to `optimizeDeps.include`.

## Tailwind

Use the public preset with Tailwind 3.4. Keep PostCSS configuration in the
consuming application. Tailwind 4 is outside the supported toolchain.

```js
// tailwind.config.js
import ngbPreset from '@ngbplatform/ui/tailwind-preset'

export default {
  presets: [ngbPreset],
  content: [
    './index.html',
    './src/**/*.{vue,js,ts,jsx,tsx}',
    './node_modules/@ngbplatform/ui/src/**/*.{vue,js,ts,jsx,tsx}',
  ],
}
```

```js
// postcss.config.js
export default {
  plugins: {
    tailwindcss: {},
    autoprefixer: {},
  },
}
```

The preset exports the same `ngbTailwindBaseConfig` as its default export and
contains design-token mappings, dark-mode configuration and shared theme values.
It does not inject content paths or load repository files. The generated starter
pins a tested Vite 7 / PostCSS 8 / Vue 3.5 toolchain in its lockfile.

Supported package entry points are the root, `contracts`, `editor`, `layout`,
`lazy`, `navigation`, `work-center`, `styles`, `vite-public-assets` and
`tailwind-preset`. Other source paths are implementation details.

## Navigation and page context

AI Assistant uses the shared navigation configuration for page identity and
source links. Standard catalog, document, report and general journal routes work
without additional configuration, including saved records in compact editors.
Applications with custom routes supply their semantic context at bootstrap:

```ts
import { configureNgbNavigation, NgbSourceKinds } from '@ngbplatform/ui/navigation'

configureNgbNavigation({
  resolvePageContext(route) {
    if (route.name !== 'customer') return undefined
    return {
      kind: NgbSourceKinds.Catalog,
      typeCode: 'example.customer',
      id: String(route.params.customerId),
    }
  },
})
```

Return `undefined` to use the standard route adapter, or `null` to disable page
context for a route. Only validated `kind`, `typeCode` and `id` are sent; loaded
rows and other page state are excluded. This context does not grant access to data.

Source links use the record builders already registered through
`configureNgbEditor({ routing: ... })`. A `configureNgbNavigation.resolveTarget`
override takes precedence. Add `resolvePageContext` to the application's existing
navigation configuration; calling `configureNgbNavigation` replaces that configuration.

## License

Apache-2.0.
