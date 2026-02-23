import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AuthProvider } from 'react-oidc-context'
import { oidcConfig, AUTH_ENABLED } from './auth/config'
import './index.css'
import App from './App.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    {AUTH_ENABLED ? (
      <AuthProvider {...oidcConfig}>
        <App />
      </AuthProvider>
    ) : (
      <App />
    )}
  </StrictMode>,
)
