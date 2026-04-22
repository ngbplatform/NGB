import { ngbTailwindBaseConfig } from '../tailwind.shared.config.js'

/** @type {import('tailwindcss').Config} */
export default {
  ...ngbTailwindBaseConfig,
  content: {
    relative: true,
    files: [
      './index.html',
      './src/**/*.{vue,ts}',
      '../ngb-ui-framework/src/**/*.{vue,ts}',
    ],
  },
}
