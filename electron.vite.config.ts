import { defineConfig } from 'electron-vite'
import react from '@vitejs/plugin-react'
import { resolve } from 'node:path'

const alias = {
  '@domain': resolve(__dirname, 'src/domain'),
  '@application': resolve(__dirname, 'src/application'),
  '@infrastructure': resolve(__dirname, 'src/infrastructure'),
  '@presentation': resolve(__dirname, 'src/presentation'),
  '@shared': resolve(__dirname, 'src/shared')
}

export default defineConfig({
  main: {
    resolve: { alias },
    build: {
      rollupOptions: { input: { index: resolve(__dirname, 'src/main/index.ts') } }
    }
  },
  preload: {
    resolve: { alias },
    build: {
      rollupOptions: { input: { index: resolve(__dirname, 'src/preload/index.ts') } }
    }
  },
  renderer: {
    root: resolve(__dirname, 'src/presentation'),
    resolve: { alias },
    plugins: [react()],
    build: {
      rollupOptions: { input: { index: resolve(__dirname, 'src/presentation/index.html') } }
    }
  }
})
