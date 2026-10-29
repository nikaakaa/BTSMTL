import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const host = 'http://127.0.0.1:5367';

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5368,
    strictPort: true,
    proxy: {
      '/api': { target: host, ws: true, changeOrigin: false },
    },
  },
});
