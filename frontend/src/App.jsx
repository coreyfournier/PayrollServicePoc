import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Layout from './components/Layout';
import ProtectedRoute from './auth/ProtectedRoute';
import AuthTokenProvider from './auth/AuthTokenProvider';
import { AUTH_ENABLED } from './auth/config';
import EmployeeList from './pages/EmployeeList';
import EmployeeDetail from './pages/EmployeeDetail';
import './index.css';

function AppRoutes() {
  return (
    <BrowserRouter>
      <Layout>
        <Routes>
          <Route path="/" element={<EmployeeList />} />
          <Route path="/employees/:id" element={<EmployeeDetail />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  );
}

function App() {
  if (!AUTH_ENABLED) {
    return <AppRoutes />;
  }

  return (
    <ProtectedRoute>
      <AuthTokenProvider>
        <AppRoutes />
      </AuthTokenProvider>
    </ProtectedRoute>
  );
}

export default App;
