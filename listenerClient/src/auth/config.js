export const AUTH_ENABLED = import.meta.env.VITE_AUTH_ENABLED !== 'false';

export const oidcConfig = {
  authority: 'http://localhost:8180/realms/listener-client',
  client_id: 'listener-frontend',
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile email',
  automaticSilentRenew: true,
};
