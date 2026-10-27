import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 刻意不配置 proxy：本项目的验收点之一是「React 在浏览器中能够正常访问后端」，
// 走 proxy 会让跨域问题被绕过，也就证明不了 Go 侧的 CORS 真的生效。
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // 端口被占用时直接报错而不是自动换端口，避免打印的地址和实际不符
    strictPort: false,
  },
})
