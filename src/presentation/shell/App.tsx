import { useState } from 'react'

/**
 * Shell mínimo de la etapa 0: verifica el arranque de Electron, el tema y la
 * tipografía. La interfaz completa se incorpora en las etapas posteriores.
 */
export function App(): React.JSX.Element {
  const [ready] = useState(true)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <nav style={{ background: 'var(--tt-chrome)', borderBottom: '1px solid var(--tt-border-light)', padding: '3px 8px' }}>
        TinyTab Statistical Software
      </nav>
      <main style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, flexDirection: 'column' }}>
        <h1 style={{ margin: 0, color: 'var(--tt-accent)' }}>TinyTab</h1>
        <p>Software estadístico libre — GPL-3.0-or-later</p>
        <p style={{ color: 'var(--tt-text-muted)' }}>{ready ? 'Entorno Electron + React + TypeScript listo.' : 'Inicializando…'}</p>
      </main>
      <footer style={{ background: 'var(--tt-chrome)', borderTop: '1px solid var(--tt-border-light)', padding: '2px 8px', color: 'var(--tt-text-muted)' }}>
        Etapa 0 — estructura del proyecto
      </footer>
    </div>
  )
}
