import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],

  server: {
    port: 3000,
    // Listen on all interfaces so the container is reachable from the host.
    host: true,
    strictPort: true,
  },

  preview: {
    port: 3000,
    host: true,
    strictPort: true,
  },

  optimizeDeps: {
    // The workspace packages are raw TypeScript rather than built bundles, so
    // Vite must not try to pre-bundle them as external dependencies.
    exclude: ['@ecommerce/shared', '@ecommerce/design-tokens'],
  },

  build: {
    outDir: 'dist',
    sourcemap: true,
  },
});
