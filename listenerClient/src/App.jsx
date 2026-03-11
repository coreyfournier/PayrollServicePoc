import { Provider as UrqlProvider } from 'urql';
import { urqlClient } from './graphql/client';
import EmployeeList from './components/EmployeeList';

export default function App() {
  return (
    <UrqlProvider value={urqlClient}>
      <div className="app">
        <div className="app-header">
          <div className="header-content">
            <div className="header-brand">
              <h1>PayrollPro Live</h1>
              <p>Real-time employee and transfer monitoring</p>
            </div>
          </div>
        </div>
        <div className="app-body">
          <EmployeeList />
        </div>
      </div>
    </UrqlProvider>
  );
}
