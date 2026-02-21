import { Provider as UrqlProvider } from 'urql';
import { urqlClient } from './graphql/client';
import EmployeeList from './components/EmployeeList';

export default function App() {
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
