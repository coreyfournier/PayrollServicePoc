import { NavLink } from 'react-router-dom';
import { Users, ArrowRightLeft, DollarSign } from 'lucide-react';

function Layout({ children }) {
  return (
    <div className="app-container">
      <aside className="sidebar">
        <div className="sidebar-header">
          <div className="sidebar-logo">
            <DollarSign />
            <span>PayrollPro</span>
          </div>
        </div>
        <nav className="sidebar-nav">
          <NavLink to="/" end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Users />
            <span>Employees</span>
          </NavLink>
          <NavLink to="/transfers" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <ArrowRightLeft />
            <span>Transfers</span>
          </NavLink>
        </nav>
      </aside>
      <main className="main-content">
        {children}
      </main>
    </div>
  );
}

export default Layout;
