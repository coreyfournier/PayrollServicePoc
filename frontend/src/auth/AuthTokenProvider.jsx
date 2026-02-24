import { useEffect } from 'react';
import { useAuth } from 'react-oidc-context';
import { setTokenGetter } from '../api';

export default function AuthTokenProvider({ children }) {
  const auth = useAuth();

  useEffect(() => {
    if (auth.isAuthenticated) {
      setTokenGetter(() => auth.user?.access_token);
    }
  }, [auth.isAuthenticated, auth.user]);

  return children;
}
