import { ngbTailwindBaseConfig } from './tailwind.base.js'

export default {
  ...ngbTailwindBaseConfig,
  content: ['./index.html', './src/**/*.{vue,ts}', './node_modules/@ngbplatform/ui/src/**/*.{vue,ts}'],
}
