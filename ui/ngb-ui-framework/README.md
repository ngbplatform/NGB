# @ngbplatform/ui

Reusable Vue UI building blocks for NGB Platform vertical applications.

## Install

```bash
npm install --save-exact @ngbplatform/ui@3.0.0
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
})
```

## Tailwind

Applications that process package source with Tailwind should include package source files in their `content.files` list:

```js
'./node_modules/@ngbplatform/ui/src/**/*.{vue,ts}'
```

## License

Apache-2.0.
