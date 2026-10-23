import js from '@eslint/js'
import globals from 'globals'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  { ignores: ['out/**', 'dist/**', 'release/**', 'coverage/**', 'node_modules/**', 'playwright-report/**', 'test-results/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2023,
      globals: { ...globals.node, ...globals.browser }
    },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            { name: 'electron', message: 'Solo src/main y src/preload pueden importar electron.' },
            { name: 'react', message: 'El dominio y la aplicación no conocen React.' }
          ]
        }
      ]
    }
  },
  {
    files: ['src/main/**/*.ts', 'src/preload/**/*.ts', 'src/infrastructure/**/*.ts', 'scripts/**/*.mjs', '*.config.ts'],
    languageOptions: { globals: { ...globals.node } },
    rules: { 'no-restricted-imports': 'off' }
  },
  {
    files: ['src/domain/**/*.ts', 'src/application/**/*.ts', 'src/shared/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            { group: ['electron', 'react', 'react-dom', 'zustand', '**/infrastructure/**', '**/presentation/**'], message: 'Capa interna sin dependencias de UI/Electron.' }
          ]
        }
      ]
    }
  },
  {
    files: ['src/presentation/**/*.{ts,tsx}', 'tests/**/*.{ts,tsx}'],
    rules: { 'no-restricted-imports': 'off' }
  }
)
