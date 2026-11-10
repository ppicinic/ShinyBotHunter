import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { initTheme, windowNameFromSearch } from './utils/theme'

initTheme(windowNameFromSearch(window.location.search))

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)
