import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 前端开发服务器：默认 5173，host 打开以便容器/局域网访问
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true
  },
  test: {
    globals: true,
    environment: 'happy-dom',
    setupFiles: []
  }
})
