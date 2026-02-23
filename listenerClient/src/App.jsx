import { useMemo } from 'react';
import { useAuth } from 'react-oidc-context';
import { Provider as UrqlProvider } from 'urql';
import { createAuthenticatedClient, createUnauthenticatedClient } from './graphql/client';
import { AUTH_ENABLED } from './auth/config';
import EmployeeList from './components/EmployeeList';

function AuthenticatedApp() {
  const auth = useAuth();

  const urqlClient = useMemo(() => {
    if (!auth.isAuthenticated) return null;
    return createAuthenticatedClient(() => auth.user?.access_token);
  }, [auth.isAuthenticated, auth.user]);

  if (auth.isLoading) {
    return (
      <div className="app">
        <header>
          <h1>Employee Change Listener</h1>
        </header>
        <main style={{ textAlign: 'center', padding: '48px' }}>
          <div className="loading-spinner" />
        </main>
      </div>
    );
  }

  if (!auth.isAuthenticated) {
    return (
      <div className="app">
        <header>
          <h1>Employee Change Listener</h1>
        </header>
        <main style={{ textAlign: 'center', padding: '48px' }}>
          <button className="login-btn" onClick={() => auth.signinRedirect()}>
            Sign in to continue
          </button>
        </main>
      </div>
    );
  }

  return (
    <UrqlProvider value={urqlClient}>
      <div className="app">
        <header>
          <h1>Employee Change Listener</h1>
          <div className="auth-bar">
            <span className="auth-user">{auth.user?.profile?.preferred_username}</span>
            <button className="logout-btn" onClick={() => auth.signoutRedirect()}>
              Sign out
            </button>
          </div>
        </header>
        <main>
          <EmployeeList />
        </main>
      </div>
    </UrqlProvider>
  );
}

function UnauthenticatedApp() {
  const urqlClient = useMemo(() => createUnauthenticatedClient(), []);

  return (
    <UrqlProvider value={urqlClient}>
      <div className="app">
        <header>
          <h1>Employee Change Listener</h1>
        </header>
        <main>
          <EmployeeList />
        </main>
      </div>
    </UrqlProvider>
  );
}

export default function App() {
  return AUTH_ENABLED ? <AuthenticatedApp /> : <UnauthenticatedApp />;
}
