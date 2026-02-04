import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
  ArrowLeft, Mail, Calendar, DollarSign, Clock, FileText,
  Building, Plus, Edit, Trash2, X, Play, Square
} from 'lucide-react';
import {
  getEmployee, getTimeEntries, getTaxInfo, getDeductions,
  clockIn, clockOut, createTaxInfo, updateTaxInfo,
  createDeduction, updateDeduction, deleteDeduction
} from '../api';
import { format, formatDistanceToNow } from 'date-fns';

const PAY_TYPES = { 1: 'Hourly', 2: 'Salary' };
const DEDUCTION_TYPES = {
  1: 'Health Insurance',
  2: 'Dental Insurance',
  3: 'Vision Insurance',
  4: '401(k)',
  5: 'Life Insurance',
  99: 'Other'
};

function EmployeeDetail() {
  const { id } = useParams();
  const [employee, setEmployee] = useState(null);
  const [timeEntries, setTimeEntries] = useState([]);
  const [taxInfo, setTaxInfo] = useState(null);
  const [deductions, setDeductions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('overview');
  const [showTaxModal, setShowTaxModal] = useState(false);
  const [showDeductionModal, setShowDeductionModal] = useState(false);
  const [editingDeduction, setEditingDeduction] = useState(null);
  const [clockedIn, setClockedIn] = useState(false);

  const [taxForm, setTaxForm] = useState({
    federalFilingStatus: 'Single',
    federalAllowances: 0,
    additionalFederalWithholding: 0,
    state: '',
    stateFilingStatus: 'Single',
    stateAllowances: 0,
    additionalStateWithholding: 0,
  });

  const [deductionForm, setDeductionForm] = useState({
    deductionType: 1,
    description: '',
    amount: '',
    isPercentage: false,
  });

  useEffect(() => {
    loadAllData();
  }, [id]);

  const loadAllData = async () => {
    try {
      const [empRes, timeRes, taxRes, dedRes] = await Promise.all([
        getEmployee(id),
        getTimeEntries(id),
        getTaxInfo(id).catch(() => ({ data: null })),
        getDeductions(id),
      ]);

      setEmployee(empRes.data);
      setTimeEntries(timeRes.data);
      setTaxInfo(taxRes.data);
      setDeductions(dedRes.data);

      // Check if currently clocked in
      const activeEntry = timeRes.data.find(e => !e.clockOut);
      setClockedIn(!!activeEntry);

      if (taxRes.data) {
        setTaxForm({
          federalFilingStatus: taxRes.data.federalFilingStatus,
          federalAllowances: taxRes.data.federalAllowances,
          additionalFederalWithholding: taxRes.data.additionalFederalWithholding,
          state: taxRes.data.state,
          stateFilingStatus: taxRes.data.stateFilingStatus,
          stateAllowances: taxRes.data.stateAllowances,
          additionalStateWithholding: taxRes.data.additionalStateWithholding,
        });
      }
    } catch (error) {
      console.error('Error loading data:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleClockIn = async () => {
    try {
      await clockIn(id);
      setClockedIn(true);
      loadAllData();
    } catch (error) {
      console.error('Error clocking in:', error);
      alert(error.response?.data || 'Error clocking in');
    }
  };

  const handleClockOut = async () => {
    try {
      await clockOut(id);
      setClockedIn(false);
      loadAllData();
    } catch (error) {
      console.error('Error clocking out:', error);
      alert(error.response?.data || 'Error clocking out');
    }
  };

  const handleSaveTaxInfo = async (e) => {
    e.preventDefault();
    try {
      const payload = {
        ...taxForm,
        employeeId: id,
        federalAllowances: parseInt(taxForm.federalAllowances),
        additionalFederalWithholding: parseFloat(taxForm.additionalFederalWithholding),
        stateAllowances: parseInt(taxForm.stateAllowances),
        additionalStateWithholding: parseFloat(taxForm.additionalStateWithholding),
      };

      if (taxInfo) {
        await updateTaxInfo(id, payload);
      } else {
        await createTaxInfo(payload);
      }
      setShowTaxModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error saving tax info:', error);
    }
  };

  const handleOpenDeductionModal = (deduction = null) => {
    if (deduction) {
      setEditingDeduction(deduction);
      setDeductionForm({
        deductionType: deduction.deductionType,
        description: deduction.description,
        amount: deduction.amount,
        isPercentage: deduction.isPercentage,
      });
    } else {
      setEditingDeduction(null);
      setDeductionForm({
        deductionType: 1,
        description: '',
        amount: '',
        isPercentage: false,
      });
    }
    setShowDeductionModal(true);
  };

  const handleSaveDeduction = async (e) => {
    e.preventDefault();
    try {
      const payload = {
        ...deductionForm,
        employeeId: id,
        deductionType: parseInt(deductionForm.deductionType),
        amount: parseFloat(deductionForm.amount),
      };

      if (editingDeduction) {
        await updateDeduction(editingDeduction.id, payload);
      } else {
        await createDeduction(payload);
      }
      setShowDeductionModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error saving deduction:', error);
    }
  };

  const handleDeleteDeduction = async (dedId) => {
    if (window.confirm('Are you sure you want to remove this deduction?')) {
      try {
        await deleteDeduction(dedId);
        loadAllData();
      } catch (error) {
        console.error('Error deleting deduction:', error);
      }
    }
  };

  const formatCurrency = (amount, payType) => {
    if (payType === 2) {
      return `$${amount.toLocaleString()}/year`;
    }
    return `$${amount.toFixed(2)}/hour`;
  };

  const getTotalHours = () => {
    return timeEntries.reduce((sum, entry) => sum + entry.hoursWorked, 0).toFixed(2);
  };

  if (loading) {
    return (
      <div className="loading">
        <div className="spinner"></div>
      </div>
    );
  }

  if (!employee) {
    return <div>Employee not found</div>;
  }

  return (
    <>
      <Link to="/" className="back-link">
        <ArrowLeft /> Back to Employees
      </Link>

      <div className="card" style={{ marginBottom: '24px' }}>
        <div className="employee-header">
          <div className="employee-avatar">
            {employee.firstName[0]}{employee.lastName[0]}
          </div>
          <div className="employee-info">
            <h2>{employee.firstName} {employee.lastName}</h2>
            <p>{employee.email}</p>
            <div className="employee-meta">
              <div className="employee-meta-item">
                <DollarSign />
                {formatCurrency(employee.payRate, employee.payType)}
              </div>
              <div className="employee-meta-item">
                <Building />
                {PAY_TYPES[employee.payType]}
              </div>
              <div className="employee-meta-item">
                <Calendar />
                Hired {format(new Date(employee.hireDate), 'MMM d, yyyy')}
              </div>
            </div>
          </div>
          <div style={{ marginLeft: 'auto' }}>
            {employee.payType === 1 && (
              clockedIn ? (
                <button className="btn btn-danger" onClick={handleClockOut}>
                  <Square /> Clock Out
                </button>
              ) : (
                <button className="btn btn-success" onClick={handleClockIn}>
                  <Play /> Clock In
                </button>
              )
            )}
          </div>
        </div>

        <div className="tabs">
          <button
            className={`tab ${activeTab === 'overview' ? 'active' : ''}`}
            onClick={() => setActiveTab('overview')}
          >
            Overview
          </button>
          <button
            className={`tab ${activeTab === 'time' ? 'active' : ''}`}
            onClick={() => setActiveTab('time')}
          >
            Time Entries
          </button>
          <button
            className={`tab ${activeTab === 'tax' ? 'active' : ''}`}
            onClick={() => setActiveTab('tax')}
          >
            Tax Information
          </button>
          <button
            className={`tab ${activeTab === 'deductions' ? 'active' : ''}`}
            onClick={() => setActiveTab('deductions')}
          >
            Deductions
          </button>
        </div>

        <div className="tab-content">
          {activeTab === 'overview' && (
            <div className="info-grid">
              <div className="info-item">
                <div className="info-label">Employee ID</div>
                <div className="info-value" style={{ fontSize: '13px' }}>{employee.id}</div>
              </div>
              <div className="info-item">
                <div className="info-label">Status</div>
                <div className="info-value">
                  <span className={`badge ${employee.isActive ? 'badge-success' : 'badge-danger'}`}>
                    {employee.isActive ? 'Active' : 'Inactive'}
                  </span>
                </div>
              </div>
              <div className="info-item">
                <div className="info-label">Pay Type</div>
                <div className="info-value">{PAY_TYPES[employee.payType]}</div>
              </div>
              <div className="info-item">
                <div className="info-label">Pay Rate</div>
                <div className="info-value">{formatCurrency(employee.payRate, employee.payType)}</div>
              </div>
              <div className="info-item">
                <div className="info-label">Hire Date</div>
                <div className="info-value">{format(new Date(employee.hireDate), 'MMMM d, yyyy')}</div>
              </div>
              <div className="info-item">
                <div className="info-label">Tenure</div>
                <div className="info-value">{formatDistanceToNow(new Date(employee.hireDate))}</div>
              </div>
              {employee.payType === 1 && (
                <div className="info-item">
                  <div className="info-label">Total Hours Logged</div>
                  <div className="info-value">{getTotalHours()} hrs</div>
                </div>
              )}
              <div className="info-item">
                <div className="info-label">Active Deductions</div>
                <div className="info-value">{deductions.filter(d => d.isActive).length}</div>
              </div>
            </div>
          )}

          {activeTab === 'time' && (
            <>
              {employee.payType === 1 ? (
                <div className="table-container">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Date</th>
                        <th>Clock In</th>
                        <th>Clock Out</th>
                        <th>Hours Worked</th>
                      </tr>
                    </thead>
                    <tbody>
                      {timeEntries.map((entry) => (
                        <tr key={entry.id}>
                          <td>{format(new Date(entry.clockIn), 'MMM d, yyyy')}</td>
                          <td>{format(new Date(entry.clockIn), 'h:mm a')}</td>
                          <td>
                            {entry.clockOut
                              ? format(new Date(entry.clockOut), 'h:mm a')
                              : <span className="badge badge-warning">In Progress</span>
                            }
                          </td>
                          <td>{entry.hoursWorked.toFixed(2)} hrs</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {timeEntries.length === 0 && (
                    <div className="empty-state">
                      <Clock />
                      <h3>No time entries</h3>
                      <p>Clock in to start tracking time.</p>
                    </div>
                  )}
                </div>
              ) : (
                <div className="empty-state">
                  <Clock />
                  <h3>Time tracking not available</h3>
                  <p>Time entries are only tracked for hourly employees.</p>
                </div>
              )}
            </>
          )}

          {activeTab === 'tax' && (
            <>
              <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '16px' }}>
                <button className="btn btn-primary btn-sm" onClick={() => setShowTaxModal(true)}>
                  <Edit /> {taxInfo ? 'Edit Tax Info' : 'Add Tax Info'}
                </button>
              </div>
              {taxInfo ? (
                <div className="info-grid">
                  <div className="info-item">
                    <div className="info-label">Federal Filing Status</div>
                    <div className="info-value">{taxInfo.federalFilingStatus}</div>
                  </div>
                  <div className="info-item">
                    <div className="info-label">Federal Allowances</div>
                    <div className="info-value">{taxInfo.federalAllowances}</div>
                  </div>
                  <div className="info-item">
                    <div className="info-label">Additional Federal Withholding</div>
                    <div className="info-value">${taxInfo.additionalFederalWithholding.toFixed(2)}</div>
                  </div>
                  <div className="info-item">
                    <div className="info-label">State</div>
                    <div className="info-value">{taxInfo.state}</div>
                  </div>
                  <div className="info-item">
                    <div className="info-label">State Filing Status</div>
                    <div className="info-value">{taxInfo.stateFilingStatus}</div>
                  </div>
                  <div className="info-item">
                    <div className="info-label">State Allowances</div>
                    <div className="info-value">{taxInfo.stateAllowances}</div>
                  </div>
                  <div className="info-item">
                    <div className="info-label">Additional State Withholding</div>
                    <div className="info-value">${taxInfo.additionalStateWithholding.toFixed(2)}</div>
                  </div>
                </div>
              ) : (
                <div className="empty-state">
                  <FileText />
                  <h3>No tax information</h3>
                  <p>Click "Add Tax Info" to configure tax withholdings.</p>
                </div>
              )}
            </>
          )}

          {activeTab === 'deductions' && (
            <>
              <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '16px' }}>
                <button className="btn btn-primary btn-sm" onClick={() => handleOpenDeductionModal()}>
                  <Plus /> Add Deduction
                </button>
              </div>
              <div className="table-container">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Type</th>
                      <th>Description</th>
                      <th>Amount</th>
                      <th>Status</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {deductions.map((deduction) => (
                      <tr key={deduction.id}>
                        <td>{DEDUCTION_TYPES[deduction.deductionType]}</td>
                        <td>{deduction.description}</td>
                        <td>
                          {deduction.isPercentage
                            ? `${deduction.amount}%`
                            : `$${deduction.amount.toFixed(2)}`
                          }
                        </td>
                        <td>
                          <span className={`badge ${deduction.isActive ? 'badge-success' : 'badge-danger'}`}>
                            {deduction.isActive ? 'Active' : 'Inactive'}
                          </span>
                        </td>
                        <td>
                          <div className="actions-cell">
                            <button
                              className="btn btn-secondary btn-sm btn-icon"
                              onClick={() => handleOpenDeductionModal(deduction)}
                            >
                              <Edit />
                            </button>
                            <button
                              className="btn btn-danger btn-sm btn-icon"
                              onClick={() => handleDeleteDeduction(deduction.id)}
                            >
                              <Trash2 />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {deductions.length === 0 && (
                  <div className="empty-state">
                    <DollarSign />
                    <h3>No deductions</h3>
                    <p>Click "Add Deduction" to configure payroll deductions.</p>
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      </div>

      {showTaxModal && (
        <div className="modal-overlay" onClick={() => setShowTaxModal(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">{taxInfo ? 'Edit Tax Information' : 'Add Tax Information'}</h3>
              <button className="modal-close" onClick={() => setShowTaxModal(false)}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleSaveTaxInfo}>
                <h4 style={{ marginBottom: '16px', fontSize: '14px', color: '#64748b' }}>Federal Taxes</h4>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Filing Status</label>
                    <select
                      className="form-select"
                      value={taxForm.federalFilingStatus}
                      onChange={(e) => setTaxForm({ ...taxForm, federalFilingStatus: e.target.value })}
                    >
                      <option value="Single">Single</option>
                      <option value="Married">Married</option>
                      <option value="Head of Household">Head of Household</option>
                    </select>
                  </div>
                  <div className="form-group">
                    <label className="form-label">Allowances</label>
                    <input
                      type="number"
                      className="form-input"
                      value={taxForm.federalAllowances}
                      onChange={(e) => setTaxForm({ ...taxForm, federalAllowances: e.target.value })}
                    />
                  </div>
                </div>
                <div className="form-group">
                  <label className="form-label">Additional Withholding ($)</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-input"
                    value={taxForm.additionalFederalWithholding}
                    onChange={(e) => setTaxForm({ ...taxForm, additionalFederalWithholding: e.target.value })}
                  />
                </div>

                <h4 style={{ marginBottom: '16px', marginTop: '24px', fontSize: '14px', color: '#64748b' }}>State Taxes</h4>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">State</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="e.g., CA, NY, TX"
                      value={taxForm.state}
                      onChange={(e) => setTaxForm({ ...taxForm, state: e.target.value })}
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Filing Status</label>
                    <select
                      className="form-select"
                      value={taxForm.stateFilingStatus}
                      onChange={(e) => setTaxForm({ ...taxForm, stateFilingStatus: e.target.value })}
                    >
                      <option value="Single">Single</option>
                      <option value="Married">Married</option>
                      <option value="Head of Household">Head of Household</option>
                    </select>
                  </div>
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Allowances</label>
                    <input
                      type="number"
                      className="form-input"
                      value={taxForm.stateAllowances}
                      onChange={(e) => setTaxForm({ ...taxForm, stateAllowances: e.target.value })}
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Additional Withholding ($)</label>
                    <input
                      type="number"
                      step="0.01"
                      className="form-input"
                      value={taxForm.additionalStateWithholding}
                      onChange={(e) => setTaxForm({ ...taxForm, additionalStateWithholding: e.target.value })}
                    />
                  </div>
                </div>

                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => setShowTaxModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    Save Tax Information
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {showDeductionModal && (
        <div className="modal-overlay" onClick={() => setShowDeductionModal(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">{editingDeduction ? 'Edit Deduction' : 'Add Deduction'}</h3>
              <button className="modal-close" onClick={() => setShowDeductionModal(false)}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleSaveDeduction}>
                <div className="form-group">
                  <label className="form-label">Deduction Type</label>
                  <select
                    className="form-select"
                    value={deductionForm.deductionType}
                    onChange={(e) => setDeductionForm({ ...deductionForm, deductionType: parseInt(e.target.value) })}
                  >
                    <option value={1}>Health Insurance</option>
                    <option value={2}>Dental Insurance</option>
                    <option value={3}>Vision Insurance</option>
                    <option value={4}>401(k)</option>
                    <option value={5}>Life Insurance</option>
                    <option value={99}>Other</option>
                  </select>
                </div>
                <div className="form-group">
                  <label className="form-label">Description</label>
                  <input
                    type="text"
                    className="form-input"
                    value={deductionForm.description}
                    onChange={(e) => setDeductionForm({ ...deductionForm, description: e.target.value })}
                    required
                  />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Amount</label>
                    <input
                      type="number"
                      step="0.01"
                      className="form-input"
                      value={deductionForm.amount}
                      onChange={(e) => setDeductionForm({ ...deductionForm, amount: e.target.value })}
                      required
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Amount Type</label>
                    <select
                      className="form-select"
                      value={deductionForm.isPercentage}
                      onChange={(e) => setDeductionForm({ ...deductionForm, isPercentage: e.target.value === 'true' })}
                    >
                      <option value={false}>Fixed Amount ($)</option>
                      <option value={true}>Percentage (%)</option>
                    </select>
                  </div>
                </div>

                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => setShowDeductionModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    {editingDeduction ? 'Save Changes' : 'Add Deduction'}
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

export default EmployeeDetail;
