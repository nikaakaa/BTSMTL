import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const host = 'http://127.0.0.1:5367';

// 构建产物写入宿主的 wwwroot，构建前清空；wwwroot 不入库。
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
  server: {
    host: '127.0.0.1',
    port: 5368,
    strictPort: true,
    proxy: {
      '/api': { target: host, ws: true, changeOrigin: false },
    },
  },
});
