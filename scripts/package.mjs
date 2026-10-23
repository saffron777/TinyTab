/**
 * Empaquetado robusto para Windows.
 *
 * electron-builder extrae Electron en `release/win-unpacked.tmp` y luego renombra
 * esa carpeta. En unidades como E: (o con antivirus/indexador activos) ese rename
 * puede fallar con EPERM/EBUSY porque algún proceso mantiene abierto un archivo
 * (p. ej. `resources/default_app.asar`). Para evitarlo:
 *
 *   1. Se compila en una carpeta temporal del sistema.
 *   2. Se empaqueta allí con electron-builder.
 *   3. Sólo los artefactos finales se copian a `release/`.
 *
 * Uso:
 *   node scripts/package.mjs         # instalador NSIS + portable
 *   node scripts/package.mjs --dir   # además, carpeta win-unpacked
 */
import { spawnSync } from 'node:child_process'
import { cp, mkdir, readdir, rm, stat } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

const root = resolve(process.cwd())
const dirMode = process.argv.includes('--dir')
const releaseDir = join(root, 'release')
const tempOutput = join(tmpdir(), `tinytab-release-${Date.now()}`)

function run(label, script, args) {
  console.log(`\n=== ${label} ===`)
  const result = spawnSync(process.execPath, [script, ...args], { cwd: root, stdio: 'inherit' })
  if (result.status !== 0) {
    console.error(`\n[package] Falló: ${label} (código ${result.status ?? result.signal})`)
    process.exit(result.status ?? 1)
  }
}

const electronVite = join(root, 'node_modules', 'electron-vite', 'bin', 'electron-vite.js')
const electronBuilder = join(root, 'node_modules', 'electron-builder', 'cli.js')
for (const required of [electronVite, electronBuilder]) {
  try {
    await stat(required)
  } catch {
    console.error(`[package] No se encontró ${required}. Ejecute "npm install" primero.`)
    process.exit(1)
  }
}

console.log(`[package] Compilando en ${tempOutput}`)
run('electron-vite build', electronVite, ['build'])

const builderArgs = ['--win', '--x64', `--config.directories.output=${tempOutput}`]
if (dirMode) builderArgs.push('--dir')
run('electron-builder', electronBuilder, builderArgs)

await mkdir(releaseDir, { recursive: true })

// Limpieza best-effort de restos bloqueados de ejecuciones anteriores.
for (const stale of ['win-unpacked.tmp']) {
  try {
    await rm(join(releaseDir, stale), { recursive: true, force: true })
  } catch {
    console.warn(`[package] No se pudo limpiar release/${stale} (bloqueado); se ignora.`)
  }
}

const artifacts = (await readdir(tempOutput, { withFileTypes: true })).filter(
  (entry) => entry.isFile() && /\.(exe|blockmap|yml)$/.test(entry.name)
)
const copied = []
for (const artifact of artifacts) {
  const destination = join(releaseDir, artifact.name)
  try {
    await rm(destination, { force: true })
  } catch {
    // Si existe y está bloqueado, cp fallará con un mensaje claro.
  }
  await cp(join(tempOutput, artifact.name), destination)
  copied.push(artifact.name)
}

if (dirMode) {
  const unpacked = join(tempOutput, 'win-unpacked')
  try {
    await stat(unpacked)
    await rm(join(releaseDir, 'win-unpacked'), { recursive: true, force: true })
    await cp(unpacked, join(releaseDir, 'win-unpacked'), { recursive: true })
    copied.push('win-unpacked/')
  } catch (error) {
    console.warn(`[package] No se pudo copiar win-unpacked: ${error.message}`)
  }
}

try {
  await rm(tempOutput, { recursive: true, force: true })
} catch {
  console.warn(`[package] No se pudo borrar la carpeta temporal ${tempOutput}; puede eliminarla a mano.`)
}

console.log('\n[package] Artefactos en release/:')
for (const name of copied) console.log(`  - ${name}`)
console.log('\n[package] OK')
