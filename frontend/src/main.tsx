import { StrictMode } from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { ThemeProvider } from './theme'
import './index.css'
import { registerServiceWorker } from './lib/sw-register'

registerServiceWorker().catch((error) => {
  console.error('Service Worker 注册失败:', error)
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <App />
    </ThemeProvider>
  </StrictMode>
)
