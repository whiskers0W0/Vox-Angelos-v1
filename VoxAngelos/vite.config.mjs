import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  define: {
    'process.env.NODE_ENV': JSON.stringify('production')
  },
  build: {
    outDir: 'wwwroot/dist/liveness',
    emptyOutDir: true,
    lib: {
      entry: 'ClientApp/liveness.jsx',
      formats: ['iife'],
      name: 'VoxLiveness',
      fileName: () => 'liveness.js'
    }
  }
});
