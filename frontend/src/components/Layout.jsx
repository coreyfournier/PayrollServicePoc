import { NavLink } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { Users, DollarSign, LogOut, User } from 'lucide-react';
import { AUTH_ENABLED } from '../auth/config';

function AuthSidebarFooter() {
  const auth = useAuth();

  return (
    <div className="sidebar-footer">
      <div className="user-info">
        <User size={16} />
        <span>{auth.user?.profile?.preferred_username ?? 'User'}</span>
      </div>
      <button
        className="logout-btn"
        onClick={() => auth.signoutRedirect()}
        title="Sign out"
      >
        <LogOut size={16} />
      </button>
    </div>
  );
}

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
          <NavLink to="/" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Users />
            <span>Employees</span>
          </NavLink>
        </nav>
        {AUTH_ENABLED && <AuthSidebarFooter />}
      </aside>
      <main className="main-content">
        {children}
      </main>
    </div>
  );
}

export default Layout;
