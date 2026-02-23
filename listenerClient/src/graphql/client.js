import { createClient, cacheExchange, fetchExchange, subscriptionExchange } from 'urql';
import { createClient as createWSClient } from 'graphql-ws';

function createWSClientWithOptions(getToken) {
  return createWSClient({
    url: () => {
      const token = getToken?.();
      const base = 'ws://localhost:5001/graphql';
      return token ? `${base}?access_token=${token}` : base;
    },
    retryAttempts: 10,
    shouldRetry: () => true,
    connectionParams: () => {
      const token = getToken?.();
      return token ? { Authorization: `Bearer ${token}` } : {};
    },
    on: {
      connected: () => console.log('WebSocket connected'),
      closed: (event) => console.log('WebSocket closed', event),
      error: (error) => console.error('WebSocket error', error),
      connecting: () => console.log('WebSocket connecting...'),
    },
  });
}

export function createAuthenticatedClient(getToken) {
  const wsClient = createWSClientWithOptions(getToken);

  return createClient({
    url: 'http://localhost:5001/graphql',
    fetchOptions: () => {
      const token = getToken();
      return token
        ? { headers: { Authorization: `Bearer ${token}` } }
        : {};
    },
    exchanges: [
      cacheExchange,
      fetchExchange,
      subscriptionExchange({
        forwardSubscription: (operation) => ({
          subscribe: (sink) => ({
            unsubscribe: wsClient.subscribe(operation, sink),
          }),
        }),
      }),
    ],
  });
}

export function createUnauthenticatedClient() {
  const wsClient = createWSClientWithOptions(null);

  return createClient({
    url: 'http://localhost:5001/graphql',
    exchanges: [
      cacheExchange,
      fetchExchange,
      subscriptionExchange({
        forwardSubscription: (operation) => ({
          subscribe: (sink) => ({
            unsubscribe: wsClient.subscribe(operation, sink),
          }),
        }),
      }),
    ],
  });
}
