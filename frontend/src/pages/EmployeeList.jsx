import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Search, Plus, Users, DollarSign, Clock, FileText, Edit, Trash2, X } from 'lucide-react';
import { getEmployees, createEmployee, updateEmployee, deleteEmployee, getEwaBalance } from '../api';
import { format } from 'date-fns';

const PAY_TYPES = { 1: 'Hourly', 2: 'Salary' };

function EmployeeList() {
  const navigate = useNavigate();
  const [employees, setEmployees] = useState([]);
  const [loading, setLoading] = useState(true);
  const [searchTerm, setSearchTerm] = useState('');
  const [balances, setBalances] = useState({});
  const [showModal, setShowModal] = useState(false);
  const [editingEmployee, setEditingEmployee] = useState(null);
  const [formData, setFormData] = useState({
    firstName: '',
    lastName: '',
    email: '',
    payType: 1,
    payRate: '',
    hireDate: '',
  });

  useEffect(() => {
    loadEmployees();
  }, []);

  const loadEmployees = async () => {
    try {
      const response = await getEmployees();
      setEmployees(response.data);

      // Fetch EWA balances for all employees in parallel
      const balanceResults = await Promise.allSettled(
        response.data.map(emp => getEwaBalance(emp.id))
      );
      const balanceMap = {};
      response.data.forEach((emp, index) => {
        const result = balanceResults[index];
        if (result.status === 'fulfilled') {
          balanceMap[emp.id] = { available: true, amount: result.value.data.finalBalance ?? 0 };
        } else {
          balanceMap[emp.id] = { available: false };
        }
      });
      setBalances(balanceMap);
    } catch (error) {
      console.error('Error loading employees:', error);
    } finally {
      setLoading(false);
    }
  };

  const filteredEmployees = employees.filter(emp => {
    const search = searchTerm.toLowerCase();
    return (
      emp.firstName.toLowerCase().includes(search) ||
      emp.lastName.toLowerCase().includes(search) ||
      emp.email.toLowerCase().includes(search) ||
      emp.id.toLowerCase().includes(search)
    );
  });

  const activeEmployees = employees.filter(e => e.isActive);
  const hourlyEmployees = employees.filter(e => e.payType === 1);
  const salaryEmployees = employees.filter(e => e.payType === 2);

  const handleOpenModal = (employee = null) => {
    if (employee) {
      setEditingEmployee(employee);
      setFormData({
        firstName: employee.firstName,
        lastName: employee.lastName,
        email: employee.email,
        payType: employee.payType,
        payRate: employee.payRate,
        hireDate: employee.hireDate.split('T')[0],
      });
    } else {
      setEditingEmployee(null);
      setFormData({
        firstName: '',
        lastName: '',
        email: '',
        payType: 1,
        payRate: '',
        hireDate: new Date().toISOString().split('T')[0],
      });
    }
    setShowModal(true);
  };

  const handleCloseModal = () => {
    setShowModal(false);
    setEditingEmployee(null);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    try {
      const payload = {
        ...formData,
        payType: parseInt(formData.payType),
        payRate: parseFloat(formData.payRate),
        hireDate: new Date(formData.hireDate).toISOString(),
      };

      if (editingEmployee) {
        await updateEmployee(editingEmployee.id, payload);
      } else {
        await createEmployee(payload);
      }
      handleCloseModal();
      loadEmployees();
    } catch (error) {
      console.error('Error saving employee:', error);
    }
  };

  const handleDelete = async (id, e) => {
    e.stopPropagation();
    if (window.confirm('Are you sure you want to deactivate this employee?')) {
      try {
        await deleteEmployee(id);
        loadEmployees();
      } catch (error) {
        console.error('Error deleting employee:', error);
      }
    }
  };

  const formatCurrency = (amount, payType) => {
    if (payType === 2) {
      return `$${amount.toLocaleString()}/yr`;
    }
    return `$${amount.toFixed(2)}/hr`;
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
      </div>
    );
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Employees</h1>
          <p className="page-subtitle">Manage your workforce and payroll information</p>
        </div>
        <button className="btn btn-primary" onClick={() => handleOpenModal()}>
          <Plus /> Add Employee
        </button>
      </div>

      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-icon blue">
            <Users />
          </div>
          <div className="stat-value">{activeEmployees.length}</div>
          <div className="stat-label">Active Employees</div>
        </div>
        <div className="stat-card">
          <div className="stat-icon green">
            <Clock />
          </div>
          <div className="stat-value">{hourlyEmployees.length}</div>
          <div className="stat-label">Hourly Workers</div>
        </div>
        <div className="stat-card">
          <div className="stat-icon purple">
            <DollarSign />
          </div>
          <div className="stat-value">{salaryEmployees.length}</div>
          <div className="stat-label">Salaried Employees</div>
        </div>
        <div className="stat-card">
          <div className="stat-icon orange">
            <FileText />
          </div>
          <div className="stat-value">{employees.length}</div>
          <div className="stat-label">Total Records</div>
        </div>
      </div>

      <div className="card">
        <div className="card-header">
          <h2 className="card-title">Employee Directory</h2>
          <div className="search-container">
            <Search className="search-icon" />
            <input
              type="text"
              className="search-input"
              placeholder="Search by name, email, or ID..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
          </div>
        </div>
        <div className="table-container">
          <table className="table">
            <thead>
              <tr>
                <th>Employee</th>
                <th>Email</th>
                <th>Pay Type</th>
                <th>Pay Rate</th>
                <th>Hire Date</th>
                <th>Status</th>
                <th>EWA Balance</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredEmployees.map((employee) => (
                <tr
                  key={employee.id}
                  className="clickable"
                  onClick={() => navigate(`/employees/${employee.id}`)}
                >
                  <td>
                    <strong>{employee.firstName} {employee.lastName}</strong>
                  </td>
                  <td>{employee.email}</td>
                  <td>
                    <span className={`badge ${employee.payType === 1 ? 'badge-info' : 'badge-secondary'}`}>
                      {PAY_TYPES[employee.payType]}
                    </span>
                  </td>
                  <td>{formatCurrency(employee.payRate, employee.payType)}</td>
                  <td>{format(new Date(employee.hireDate), 'MMM d, yyyy')}</td>
                  <td>
                    <span className={`badge ${employee.isActive ? 'badge-success' : 'badge-danger'}`}>
                      {employee.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td>
                    {balances[employee.id]
                      ? balances[employee.id].available
                        ? <span style={{ color: 'var(--success)', fontWeight: 600 }}>${balances[employee.id].amount.toLocaleString()}</span>
                        : <span style={{ color: 'var(--text-muted)' }}>$0</span>
                      : <span style={{ color: 'var(--text-muted)' }}>&mdash;</span>
                    }
                  </td>
                  <td>
                    <div className="actions-cell">
                      <button
                        className="btn btn-secondary btn-sm btn-icon"
                        onClick={(e) => { e.stopPropagation(); handleOpenModal(employee); }}
                        title="Edit"
                      >
                        <Edit />
                      </button>
                      <button
                        className="btn btn-danger btn-sm btn-icon"
                        onClick={(e) => handleDelete(employee.id, e)}
                        title="Deactivate"
                      >
                        <Trash2 />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {filteredEmployees.length === 0 && (
            <div className="empty-state">
              <Users />
              <h3>No employees found</h3>
              <p>Try adjusting your search or add a new employee.</p>
            </div>
          )}
        </div>
      </div>

      {showModal && (
        <div className="modal-overlay" onClick={handleCloseModal}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">
                {editingEmployee ? 'Edit Employee' : 'Add New Employee'}
              </h3>
              <button className="modal-close" onClick={handleCloseModal}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleSubmit}>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">First Name</label>
                    <input
                      type="text"
                      className="form-input"
                      value={formData.firstName}
                      onChange={(e) => setFormData({ ...formData, firstName: e.target.value })}
                      required
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Last Name</label>
                    <input
                      type="text"
                      className="form-input"
                      value={formData.lastName}
                      onChange={(e) => setFormData({ ...formData, lastName: e.target.value })}
                      required
                    />
                  </div>
                </div>
                <div className="form-group">
                  <label className="form-label">Email</label>
                  <input
                    type="email"
                    className="form-input"
                    value={formData.email}
                    onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                    required
                  />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Pay Type</label>
                    <select
                      className="form-select"
                      value={formData.payType}
                      onChange={(e) => setFormData({ ...formData, payType: parseInt(e.target.value) })}
                    >
                      <option value={1}>Hourly</option>
                      <option value={2}>Salary</option>
                    </select>
                  </div>
                  <div className="form-group">
                    <label className="form-label">
                      Pay Rate {formData.payType === 1 ? '($/hr)' : '($/yr)'}
                    </label>
                    <input
                      type="number"
                      className="form-input"
                      step="0.01"
                      value={formData.payRate}
                      onChange={(e) => setFormData({ ...formData, payRate: e.target.value })}
                      required
                    />
                  </div>
                </div>
                {!editingEmployee && (
                  <div className="form-group">
                    <label className="form-label">Hire Date</label>
                    <input
                      type="date"
                      className="form-input"
                      value={formData.hireDate}
                      onChange={(e) => setFormData({ ...formData, hireDate: e.target.value })}
                      required
                    />
                  </div>
                )}
                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={handleCloseModal}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    {editingEmployee ? 'Save Changes' : 'Add Employee'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

export default EmployeeList;
