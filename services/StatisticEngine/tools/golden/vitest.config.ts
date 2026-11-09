import { defineConfig } from 'vitest/config'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

/**
 * Configuración aislada del exportador de vectores dorados.
 * No se mezcla con `vitest.config.ts` de TinyTab: los tests de la app no cambian.
 *
 * Uso: npx vitest run --config services/StatisticEngine/tools/golden/vitest.config.ts
 */
const here = dirname(fileURLToPath(import.meta.url))
const repoRoot = resolve(here, '../../../..')

export default defineConfig({
  root: repoRoot,
  resolve: {
    alias: {
      '@domain': resolve(repoRoot, 'src/domain'),
      '@application': resolve(repoRoot, 'src/application'),
      '@infrastructure': resolve(repoRoot, 'src/infrastructure'),
      '@presentation': resolve(repoRoot, 'src/presentation'),
      '@shared': resolve(repoRoot, 'src/shared')
    }
  },
  test: {
    globals: false,
    environment: 'node',
    include: ['services/StatisticEngine/tools/golden/**/*.test.ts'],
    pool: 'threads',
    testTimeout: 120000,
    hookTimeout: 120000
  }
})
