import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { ngbUiFrameworkPublicAssetsPlugin } from '@ngbplatform/ui/vite-public-assets'

const api = process.env.NGB_EXAMPLE_API ?? 'http://127.0.0.1:5181'
const proxy = { '/api': api, '/hubs': { target: api, ws: true } }

export default defineConfig({
  plugins: [vue(), ngbUiFrameworkPublicAssetsPlugin()],
  optimizeDeps: { exclude: ['@ngbplatform/ui'] },
  server: { proxy },
  preview: { proxy },
})
