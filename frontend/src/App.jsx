import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Layout from './components/Layout';
import EmployeeList from './pages/EmployeeList';
import EmployeeDetail from './pages/EmployeeDetail';
import Transfers from './pages/Transfers';
import './index.css';

function App() {
  return (
    <BrowserRouter>
      <Layout>
        <Routes>
          <Route path="/" element={<EmployeeList />} />
          <Route path="/employees/:id" element={<EmployeeDetail />} />
          <Route path="/transfers" element={<Transfers />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  );
}

export default App;
