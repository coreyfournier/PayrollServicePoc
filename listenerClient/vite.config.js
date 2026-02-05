import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: 3001,
    proxy: {
      '/graphql': {
        target: 'http://localhost:5001',
        changeOrigin: true,
        ws: true
      }
    }
  }
});
