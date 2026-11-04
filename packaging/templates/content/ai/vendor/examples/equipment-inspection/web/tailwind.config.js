import ngbPreset from '@ngbplatform/ui/tailwind-preset'

export default {
  presets: [ngbPreset],
  content: [
    './index.html',
    './src/**/*.{vue,js,ts,jsx,tsx}',
    './node_modules/@ngbplatform/ui/src/**/*.{vue,js,ts,jsx,tsx}',
  ],
}
