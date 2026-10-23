import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './shell/App'
import './theme/tokens.css'
import './theme/global.css'

const container = document.getElementById('root')
if (!container) throw new Error('No se encontró el contenedor #root')

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>
)
