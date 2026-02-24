import { useAuth } from 'react-oidc-context';

export default function ProtectedRoute({ children }) {
  const auth = useAuth();

  if (auth.isLoading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
        <div className="loading-spinner" />
      </div>
    );
  }

  if (!auth.isAuthenticated) {
    auth.signinRedirect();
    return null;
  }

  return children;
}
