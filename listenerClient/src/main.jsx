import React from 'react';
import ReactDOM from 'react-dom/client';
import { AuthProvider } from 'react-oidc-context';
import { oidcConfig, AUTH_ENABLED } from './auth/config';
import App from './App';
import './index.css';

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    {AUTH_ENABLED ? (
      <AuthProvider {...oidcConfig}>
        <App />
      </AuthProvider>
    ) : (
      <App />
    )}
  </React.StrictMode>
);
