import { lazy, StrictMode, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import { getBridge } from './bridge/index.ts'
import './index.css'

// The component gallery exists only in development; the production build drops this branch.
const Gallery = import.meta.env.DEV ? lazy(() => import('./gallery/Gallery.tsx')) : null
const showGallery = Gallery !== null && new URLSearchParams(window.location.search).has('gallery')

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {showGallery ? (
      <Suspense>
        <Gallery />
      </Suspense>
    ) : (
      <App bridge={getBridge()} />
    )}
  </StrictMode>,
)
