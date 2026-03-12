import { createClient, cacheExchange, fetchExchange, subscriptionExchange } from 'urql';
import { createClient as createWSClient } from 'graphql-ws';

const wsClient = createWSClient({
  url: 'ws://localhost:5001/graphql',
  retryAttempts: 10,
  shouldRetry: () => true,
  connectionParams: () => ({}),
  on: {
    connected: () => console.log('WebSocket connected'),
    closed: (event) => console.log('WebSocket closed', event),
    error: (error) => console.error('WebSocket error', error),
    connecting: () => console.log('WebSocket connecting...'),
  },
});

export const urqlClient = createClient({
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
