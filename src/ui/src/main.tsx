import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import { getBridge } from './bridge/index.ts'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App bridge={getBridge()} />
  </StrictMode>,
)
