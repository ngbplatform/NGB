export function createPlatformUiPackageManifest(sourceManifest, version = sourceManifest.version) {
  return {
    name: '@ngbplatform/ui',
    version,
    description: 'Reusable Vue UI building blocks for NGB Platform vertical applications.',
    keywords: ['ngb', 'ngb-platform', 'vue', 'ui-framework'],
    license: 'Apache-2.0',
    author: 'NGB Platform',
    homepage: 'https://ngbplatform.com',
    repository: {
      type: 'git',
      url: 'https://github.com/ngbplatform/NGB.git',
      directory: 'ui/ngb-ui-framework',
    },
    bugs: {
      url: 'https://github.com/ngbplatform/NGB/issues',
    },
    type: 'module',
    engines: {
      node: '>=22.14.0',
    },
    sideEffects: ['./src/styles/tailwind.css', './src/**/*.vue'],
    exports: {
      '.': {
        types: './src/index.ts',
        import: './src/index.ts',
        default: './src/index.ts',
      },
      './contracts': {
        types: './src/contracts.ts',
        import: './src/contracts.ts',
        default: './src/contracts.ts',
      },
      './editor': {
        types: './src/editor.ts',
        import: './src/editor.ts',
        default: './src/editor.ts',
      },
      './layout': {
        types: './src/layout.ts',
        import: './src/layout.ts',
        default: './src/layout.ts',
      },
      './lazy': {
        types: './src/lazy.ts',
        import: './src/lazy.ts',
        default: './src/lazy.ts',
      },
      './navigation': {
        types: './src/navigation.ts',
        import: './src/navigation.ts',
        default: './src/navigation.ts',
      },
      './work-center': {
        types: './src/work-center.ts',
        import: './src/work-center.ts',
        default: './src/work-center.ts',
      },
      './styles': './src/styles/tailwind.css',
      './vite-public-assets': './vite-public-assets.js',
      './tailwind-preset': {
        types: './tailwind-preset.d.ts',
        import: './tailwind-preset.js',
        default: './tailwind-preset.js',
      },
    },
    files: ['LICENSE', 'README.md', 'public', 'src', 'vite-public-assets.js', 'vite-public-assets.d.ts', 'tailwind-preset.js', 'tailwind-preset.d.ts'],
    publishConfig: {
      access: 'public',
      registry: 'https://registry.npmjs.org/',
    },
    dependencies: {
      '@headlessui/vue': sourceManifest.dependencies['@headlessui/vue'],
      '@microsoft/signalr': sourceManifest.dependencies['@microsoft/signalr'],
    },
    peerDependencies: {
      'keycloak-js': sourceManifest.dependencies['keycloak-js'],
      pinia: sourceManifest.dependencies.pinia,
      vue: sourceManifest.dependencies.vue,
      'vue-router': sourceManifest.dependencies['vue-router'],
      vite: '>=5.0.0',
      tailwindcss: '>=3.4.0',
    },
    peerDependenciesMeta: {
      vite: { optional: true },
      tailwindcss: { optional: true },
    },
  }
}
