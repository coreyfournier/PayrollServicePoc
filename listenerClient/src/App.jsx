import { Provider as UrqlProvider } from 'urql';
import { urqlClient } from './graphql/client';
import EmployeeChangeStream from './components/EmployeeChangeStream';
import EmployeeList from './components/EmployeeList';
import { useState } from 'react';

export default function App() {
  const [activeView, setActiveView] = useState('stream');

  return (
    <UrqlProvider value={urqlClient}>
      <div className="app">
        <header>
          <h1>Employee Change Listener</h1>
          <nav className="main-nav">
            <button
              className={`nav-btn ${activeView === 'stream' ? 'active' : ''}`}
              onClick={() => setActiveView('stream')}
            >
              Change Stream
            </button>
            <button
              className={`nav-btn ${activeView === 'records' ? 'active' : ''}`}
              onClick={() => setActiveView('records')}
            >
              Employee Records
            </button>
          </nav>
        </header>
        <main>
          {activeView === 'stream' ? <EmployeeChangeStream /> : <EmployeeList />}
        </main>
      </div>
    </UrqlProvider>
  );
}
