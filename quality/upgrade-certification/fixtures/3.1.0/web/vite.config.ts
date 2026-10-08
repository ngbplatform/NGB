import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { ngbUiFrameworkPublicAssetsPlugin } from '@ngbplatform/ui/vite-public-assets'

export default defineConfig({
  plugins: [vue(), ngbUiFrameworkPublicAssetsPlugin()],
  optimizeDeps: { exclude: ['@ngbplatform/ui'] },
  server: { proxy: { '/api': 'http://127.0.0.1:5181', '/hubs': { target: 'http://127.0.0.1:5181', ws: true } } },
  preview: { proxy: { '/api': 'http://127.0.0.1:5181', '/hubs': { target: 'http://127.0.0.1:5181', ws: true } } },
})
