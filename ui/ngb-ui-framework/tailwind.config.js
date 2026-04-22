import { ngbTailwindBaseConfig } from '../tailwind.shared.config.js'

/** @type {import('tailwindcss').Config} */
export default {
  ...ngbTailwindBaseConfig,
  content: {
    relative: true,
    files: [
      './src/**/*.{vue,ts}',
      './tests/browser/**/*.{vue,ts}',
    ],
  },
}
