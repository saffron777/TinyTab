/**
 * Verificación local: typecheck + lint + tests + build.
 * Se ejecuta con `npm run verify` y también en CI.
 */
import { spawnSync } from 'node:child_process'

const npmCommand = process.platform === 'win32' ? 'npm.cmd' : 'npm'
const steps = [
  ['typecheck', ['run', 'typecheck']],
  ['lint', ['run', 'lint']],
  ['test', ['run', 'test']],
  ['build', ['run', 'build']]
]

let failed = false
for (const [name, args] of steps) {
  console.log(`\n=== ${name} ===`)
  const result = spawnSync(npmCommand, args, { stdio: 'inherit', shell: true })
  if (result.status !== 0) {
    console.error(`\n[verify] Falló: ${name}`)
    failed = true
    break
  }
}

if (failed) process.exit(1)
console.log('\n[verify] OK: typecheck, lint, tests y build en verde.')
