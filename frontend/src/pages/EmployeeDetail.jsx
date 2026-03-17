import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
  ArrowLeft, Mail, Calendar, DollarSign, Clock, FileText,
  Building, Plus, Edit, Trash2, X, Play, Square, Send, Settings
} from 'lucide-react';
import {
  getEmployee, getTimeEntries, getTaxInfo, getDeductions,
  clockIn, clockOut, createTaxInfo, updateTaxInfo,
  createDeduction, updateDeduction, deleteDeduction, updateTimeEntry,
  updateEmployee, getTransfers, getBankAccounts, createBankAccount,
  initiateTransfer, acceptTransferBalanceChange,
  getEmployeeTransferLimits, setEmployeeTransferLimits, deleteEmployeeTransferLimits,
  getTransferLimits
} from '../api';
import { format, formatDistanceToNow } from 'date-fns';

const PAY_TYPES = { 1: 'Hourly', 2: 'Salary' };
const PAY_PERIOD_EPOCH_MS = new Date('2024-01-01T00:00:00Z').getTime();
const PAY_PERIOD_DURATION_MS = 14 * 24 * 60 * 60 * 1000;
const getCurrentPayPeriod = () => Math.floor((Date.now() - PAY_PERIOD_EPOCH_MS) / PAY_PERIOD_DURATION_MS);
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
  const [showTimeEntryModal, setShowTimeEntryModal] = useState(false);
  const [editingTimeEntry, setEditingTimeEntry] = useState(null);
  const [timeEntryForm, setTimeEntryForm] = useState({
    clockIn: '',
    clockOut: '',
  });
  const [showEmployeeModal, setShowEmployeeModal] = useState(false);
  const [employeeForm, setEmployeeForm] = useState({
    firstName: '',
    lastName: '',
    email: '',
    payType: 1,
    payRate: '',
    payPeriodHours: 40,
  });
  const [transfers, setTransfers] = useState([]);
  const [bankAccounts, setBankAccounts] = useState([]);
  const [showTransferModal, setShowTransferModal] = useState(false);
  const [showBankAccountModal, setShowBankAccountModal] = useState(false);
  const [transferForm, setTransferForm] = useState({ amount: '', bankAccountId: '', payPeriodNumber: '' });
  const [bankAccountForm, setBankAccountForm] = useState({ bankName: '', accountNumberMasked: '', routingNumber: '', accountType: 1 });
  const [showLimitsModal, setShowLimitsModal] = useState(false);
  const [customLimits, setCustomLimits] = useState(null);
  const [transferLimitsData, setTransferLimitsData] = useState(null);
  const [limitsForm, setLimitsForm] = useState({
    maxTransfersPerPayPeriod: 5,
    maxAmountPerPayPeriod: 10000,
    maxTransfersPerDay: 1,
  });

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

  const loadAllData = async () => {
    try {
      const [empRes, timeRes, taxRes, dedRes, transferRes, bankRes] = await Promise.all([
        getEmployee(id),
        getTimeEntries(id),
        getTaxInfo(id).catch(() => ({ data: null })),
        getDeductions(id),
        getTransfers(id).catch(() => ({ data: [] })),
        getBankAccounts(id).catch(() => ({ data: [] })),
      ]);

      setEmployee(empRes.data);
      setTimeEntries(timeRes.data);
      setTaxInfo(taxRes.data);
      setDeductions(dedRes.data);
      setTransfers(transferRes.data);
      setBankAccounts(bankRes.data);

      // Load custom transfer limits
      try {
        const limitsRes = await getEmployeeTransferLimits(id);
        setCustomLimits(limitsRes.data);
      } catch {
        setCustomLimits(null);
      }

      // Load transfer limits usage data
      try {
        const payPeriod = getCurrentPayPeriod();
        const limitsDataRes = await getTransferLimits(id, payPeriod);
        setTransferLimitsData(limitsDataRes.data);
      } catch {
        setTransferLimitsData(null);
      }

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

  useEffect(() => {
    loadAllData();
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

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

  const handleOpenTimeEntryModal = (entry) => {
    setEditingTimeEntry(entry);
    const clockInLocal = new Date(entry.clockIn).toISOString().slice(0, 16);
    const clockOutLocal = entry.clockOut
      ? new Date(entry.clockOut).toISOString().slice(0, 16)
      : '';
    setTimeEntryForm({ clockIn: clockInLocal, clockOut: clockOutLocal });
    setShowTimeEntryModal(true);
  };

  const handleSaveTimeEntry = async (e) => {
    e.preventDefault();
    try {
      const payload = {
        clockIn: new Date(timeEntryForm.clockIn).toISOString(),
        clockOut: timeEntryForm.clockOut
          ? new Date(timeEntryForm.clockOut).toISOString()
          : null,
      };
      await updateTimeEntry(editingTimeEntry.id, payload);
      setShowTimeEntryModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error updating time entry:', error);
      alert(error.response?.data || 'Error updating time entry');
    }
  };

  const handleOpenEmployeeModal = () => {
    setEmployeeForm({
      firstName: employee.firstName,
      lastName: employee.lastName,
      email: employee.email,
      payType: employee.payType,
      payRate: employee.payRate,
      payPeriodHours: employee.payPeriodHours ?? 40,
    });
    setShowEmployeeModal(true);
  };

  const handleSaveEmployee = async (e) => {
    e.preventDefault();
    try {
      const payload = {
        ...employeeForm,
        payType: parseInt(employeeForm.payType),
        payRate: parseFloat(employeeForm.payRate),
        payPeriodHours: parseFloat(employeeForm.payPeriodHours),
        hireDate: employee.hireDate,
      };
      await updateEmployee(id, payload);
      setShowEmployeeModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error updating employee:', error);
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

  const handleOpenTransferModal = async () => {
    if (bankAccounts.length === 0) {
      alert('Please add a bank account first.');
      return;
    }
    setTransferForm({ amount: '', bankAccountId: bankAccounts[0]?.id || '', payPeriodNumber: '' });
    setShowTransferModal(true);
  };

  const handleInitiateTransfer = async (e) => {
    e.preventDefault();
    try {
      await initiateTransfer({
        employeeId: id,
        amount: parseFloat(transferForm.amount),
        payPeriodNumber: parseInt(transferForm.payPeriodNumber),
        bankAccountId: transferForm.bankAccountId,
      });
      setShowTransferModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error initiating transfer:', error);
      alert(error.response?.data?.errorMessage || error.response?.data?.reasons?.join(' ') || 'Error initiating transfer');
    }
  };

  const handleSaveBankAccount = async (e) => {
    e.preventDefault();
    try {
      await createBankAccount({
        employeeId: id,
        ...bankAccountForm,
        accountType: parseInt(bankAccountForm.accountType),
      });
      setShowBankAccountModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error creating bank account:', error);
    }
  };

  const handleOpenLimitsModal = () => {
    if (customLimits) {
      setLimitsForm({
        maxTransfersPerPayPeriod: customLimits.maxTransfersPerPayPeriod,
        maxAmountPerPayPeriod: customLimits.maxAmountPerPayPeriod,
        maxTransfersPerDay: customLimits.maxTransfersPerDay,
      });
    } else {
      setLimitsForm({ maxTransfersPerPayPeriod: 5, maxAmountPerPayPeriod: 10000, maxTransfersPerDay: 1 });
    }
    setShowLimitsModal(true);
  };

  const handleSaveLimits = async (e) => {
    e.preventDefault();
    try {
      await setEmployeeTransferLimits(id, {
        maxTransfersPerPayPeriod: parseInt(limitsForm.maxTransfersPerPayPeriod),
        maxAmountPerPayPeriod: parseFloat(limitsForm.maxAmountPerPayPeriod),
        maxTransfersPerDay: parseInt(limitsForm.maxTransfersPerDay),
      });
      setShowLimitsModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error saving transfer limits:', error);
      alert('Error saving transfer limits');
    }
  };

  const handleResetLimits = async () => {
    try {
      await deleteEmployeeTransferLimits(id);
      setCustomLimits(null);
      setShowLimitsModal(false);
      loadAllData();
    } catch (error) {
      console.error('Error resetting transfer limits:', error);
    }
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
          <div style={{ marginLeft: 'auto', display: 'flex', gap: '8px' }}>
            <button className="btn btn-secondary" onClick={handleOpenEmployeeModal}>
              <Edit /> Edit
            </button>
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
          <button
            className={`tab ${activeTab === 'transfers' ? 'active' : ''}`}
            onClick={() => setActiveTab('transfers')}
          >
            Transfers
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
              {employee.payType === 2 && (
                <div className="info-item">
                  <div className="info-label">Hours per Pay Period</div>
                  <div className="info-value">{employee.payPeriodHours} hrs</div>
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
                        <th>Actions</th>
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
                          <td>
                            <div className="actions-cell">
                              <button
                                className="btn btn-secondary btn-sm btn-icon"
                                onClick={() => handleOpenTimeEntryModal(entry)}
                              >
                                <Edit />
                              </button>
                            </div>
                          </td>
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

          {activeTab === 'transfers' && (
            <>
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '16px' }}>
                <button className="btn btn-secondary btn-sm" onClick={() => setShowBankAccountModal(true)}>
                  <Plus /> Add Bank Account
                </button>
                <button
                  className="btn btn-primary btn-sm"
                  onClick={handleOpenTransferModal}
                  disabled={transferLimitsData && !transferLimitsData.canTransfer}
                >
                  <Send /> New Transfer
                </button>
              </div>

              {bankAccounts.length > 0 && (
                <div style={{ marginBottom: '24px' }}>
                  <h4 style={{ fontSize: '14px', color: '#64748b', marginBottom: '8px' }}>Bank Accounts</h4>
                  <div className="table-container">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Bank</th>
                          <th>Account</th>
                          <th>Routing</th>
                          <th>Type</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {bankAccounts.map((acct) => (
                          <tr key={acct.id}>
                            <td>{acct.bankName}</td>
                            <td>****{acct.accountNumberMasked}</td>
                            <td>{acct.routingNumber}</td>
                            <td>{acct.accountType === 1 ? 'Checking' : 'Savings'}</td>
                            <td>
                              <span className={`badge ${acct.isActive ? 'badge-success' : 'badge-danger'}`}>
                                {acct.isActive ? 'Active' : 'Inactive'}
                              </span>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              <div style={{ marginBottom: '24px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                  <h4 style={{ fontSize: '14px', color: '#64748b', margin: 0 }}>Transfer Limits</h4>
                  <button className="btn btn-secondary btn-sm" onClick={handleOpenLimitsModal}>
                    <Settings /> {customLimits ? 'Edit Limits' : 'Customize'}
                  </button>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px' }}>
                  <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '12px' }}>
                    <div style={{ fontSize: '11px', color: '#94a3b8', textTransform: 'uppercase' }}>Per Day</div>
                    <div style={{ fontSize: '18px', fontWeight: '600', color: '#1e293b' }}>
                      {transferLimitsData ? `${transferLimitsData.transfersToday} of ` : ''}{customLimits ? customLimits.maxTransfersPerDay : 1}
                    </div>
                  </div>
                  <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '12px' }}>
                    <div style={{ fontSize: '11px', color: '#94a3b8', textTransform: 'uppercase' }}>Per Period</div>
                    <div style={{ fontSize: '18px', fontWeight: '600', color: '#1e293b' }}>
                      {transferLimitsData ? `${transferLimitsData.currentPeriodCount} of ` : ''}{customLimits ? customLimits.maxTransfersPerPayPeriod : 5}
                    </div>
                  </div>
                  <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '12px' }}>
                    <div style={{ fontSize: '11px', color: '#94a3b8', textTransform: 'uppercase' }}>Max Amount / Period</div>
                    <div style={{ fontSize: '18px', fontWeight: '600', color: '#1e293b' }}>
                      {transferLimitsData ? `$${(transferLimitsData.currentPeriodAmount ?? 0).toLocaleString()} of ` : ''}${(customLimits ? customLimits.maxAmountPerPayPeriod : 10000).toLocaleString()}
                    </div>
                  </div>
                </div>
                {customLimits && (
                  <div style={{ fontSize: '11px', color: '#3b82f6', marginTop: '6px' }}>
                    Custom limits applied for this employee
                  </div>
                )}
                {!customLimits && (
                  <div style={{ fontSize: '11px', color: '#94a3b8', marginTop: '6px' }}>
                    Using global defaults
                  </div>
                )}
                {transferLimitsData && !transferLimitsData.canTransfer && (
                  <div style={{ fontSize: '12px', color: '#ef4444', marginTop: '6px', fontWeight: '500' }}>
                    Transfer limit reached — {transferLimitsData.reasons?.join(' ')}
                  </div>
                )}
              </div>

              <h4 style={{ fontSize: '14px', color: '#64748b', marginBottom: '8px' }}>Transfer History</h4>
              <div className="table-container">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Amount</th>
                      <th>Pay Period</th>
                      <th>Status</th>
                      <th>Reference</th>
                    </tr>
                  </thead>
                  <tbody>
                    {transfers.map((t) => {
                      const status = typeof t.status === 'number'
                        ? ['', 'Initiated', 'Processing', 'Completed', 'Failed', 'AwaitingConfirmation'][t.status]
                        : t.status;
                      const isAwaiting = status === 'AwaitingConfirmation';
                      return (
                      <tr key={t.id}>
                        <td>{format(new Date(t.initiatedAt), 'MMM d, yyyy h:mm a')}</td>
                        <td>${t.amount.toFixed(2)}</td>
                        <td>{t.payPeriodNumber}</td>
                        <td>
                          <span className={`badge ${
                            status === 'Completed' ? 'badge-success' :
                            status === 'Failed' ? 'badge-danger' :
                            isAwaiting ? 'badge-info' :
                            'badge-warning'
                          }`}>
                            {isAwaiting ? 'Awaiting Confirmation' : status}
                          </span>
                          {isAwaiting && t.currentBalance != null && (
                            <div style={{ fontSize: '11px', color: '#94a3b8', marginTop: '4px' }}>
                              Balance dropped to ${t.currentBalance.toFixed(2)}
                            </div>
                          )}
                        </td>
                        <td>
                          {isAwaiting ? (
                            <div style={{ display: 'flex', gap: '6px' }}>
                              <button className="btn btn-sm btn-primary" onClick={async () => {
                                await acceptTransferBalanceChange(t.id, true);
                                const res = await getTransfers(id);
                                setTransfers(res.data);
                              }}>Accept</button>
                              <button className="btn btn-sm btn-danger" onClick={async () => {
                                await acceptTransferBalanceChange(t.id, false);
                                const res = await getTransfers(id);
                                setTransfers(res.data);
                              }}>Reject</button>
                            </div>
                          ) : (
                            t.externalReferenceId || t.failureReason || '-'
                          )}
                        </td>
                      </tr>
                      );
                    })}
                  </tbody>
                </table>
                {transfers.length === 0 && (
                  <div className="empty-state">
                    <Send />
                    <h3>No transfers</h3>
                    <p>Click "New Transfer" to initiate a transfer.</p>
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

      {showTimeEntryModal && (
        <div className="modal-overlay" onClick={() => setShowTimeEntryModal(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">Edit Time Entry</h3>
              <button className="modal-close" onClick={() => setShowTimeEntryModal(false)}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleSaveTimeEntry}>
                <div className="form-group">
                  <label className="form-label">Clock In</label>
                  <input
                    type="datetime-local"
                    className="form-input"
                    value={timeEntryForm.clockIn}
                    onChange={(e) => setTimeEntryForm({ ...timeEntryForm, clockIn: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Clock Out</label>
                  <input
                    type="datetime-local"
                    className="form-input"
                    value={timeEntryForm.clockOut}
                    onChange={(e) => setTimeEntryForm({ ...timeEntryForm, clockOut: e.target.value })}
                  />
                  <small style={{ color: '#64748b', fontSize: '12px' }}>
                    Leave empty if the entry is still in progress.
                  </small>
                </div>

                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => setShowTimeEntryModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    Save Changes
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {showEmployeeModal && (
        <div className="modal-overlay" onClick={() => setShowEmployeeModal(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">Edit Employee</h3>
              <button className="modal-close" onClick={() => setShowEmployeeModal(false)}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleSaveEmployee}>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">First Name</label>
                    <input
                      type="text"
                      className="form-input"
                      value={employeeForm.firstName}
                      onChange={(e) => setEmployeeForm({ ...employeeForm, firstName: e.target.value })}
                      required
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Last Name</label>
                    <input
                      type="text"
                      className="form-input"
                      value={employeeForm.lastName}
                      onChange={(e) => setEmployeeForm({ ...employeeForm, lastName: e.target.value })}
                      required
                    />
                  </div>
                </div>
                <div className="form-group">
                  <label className="form-label">Email</label>
                  <input
                    type="email"
                    className="form-input"
                    value={employeeForm.email}
                    onChange={(e) => setEmployeeForm({ ...employeeForm, email: e.target.value })}
                    required
                  />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Pay Type</label>
                    <select
                      className="form-select"
                      value={employeeForm.payType}
                      onChange={(e) => setEmployeeForm({ ...employeeForm, payType: parseInt(e.target.value) })}
                    >
                      <option value={1}>Hourly</option>
                      <option value={2}>Salary</option>
                    </select>
                  </div>
                  <div className="form-group">
                    <label className="form-label">
                      Pay Rate {parseInt(employeeForm.payType) === 1 ? '($/hr)' : '($/yr)'}
                    </label>
                    <input
                      type="number"
                      className="form-input"
                      step="0.01"
                      value={employeeForm.payRate}
                      onChange={(e) => setEmployeeForm({ ...employeeForm, payRate: e.target.value })}
                      required
                    />
                  </div>
                </div>
                {parseInt(employeeForm.payType) === 2 && (
                  <div className="form-group">
                    <label className="form-label">Hours per Pay Period</label>
                    <input
                      type="number"
                      className="form-input"
                      step="0.5"
                      min="0"
                      value={employeeForm.payPeriodHours}
                      onChange={(e) => setEmployeeForm({ ...employeeForm, payPeriodHours: e.target.value })}
                    />
                  </div>
                )}
                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => setShowEmployeeModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    Save Changes
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {showTransferModal && (
        <div className="modal-overlay" onClick={() => setShowTransferModal(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">Initiate Transfer</h3>
              <button className="modal-close" onClick={() => setShowTransferModal(false)}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleInitiateTransfer}>
                <div className="form-group">
                  <label className="form-label">Amount ($)</label>
                  <input
                    type="number"
                    step="0.01"
                    className="form-input"
                    value={transferForm.amount}
                    onChange={(e) => setTransferForm({ ...transferForm, amount: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Pay Period Number</label>
                  <input
                    type="number"
                    className="form-input"
                    value={transferForm.payPeriodNumber}
                    onChange={(e) => setTransferForm({ ...transferForm, payPeriodNumber: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Bank Account</label>
                  <select
                    className="form-select"
                    value={transferForm.bankAccountId}
                    onChange={(e) => setTransferForm({ ...transferForm, bankAccountId: e.target.value })}
                  >
                    {bankAccounts.filter(a => a.isActive).map((acct) => (
                      <option key={acct.id} value={acct.id}>
                        {acct.bankName} - ****{acct.accountNumberMasked}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => setShowTransferModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    Initiate Transfer
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {showLimitsModal && (
        <div className="modal-overlay" onClick={() => setShowLimitsModal(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">Transfer Limits</h3>
              <button className="modal-close" onClick={() => setShowLimitsModal(false)}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleSaveLimits}>
                <div className="form-group">
                  <label className="form-label">Max Transfers Per Day</label>
                  <input
                    type="number"
                    min="1"
                    className="form-input"
                    value={limitsForm.maxTransfersPerDay}
                    onChange={(e) => setLimitsForm({ ...limitsForm, maxTransfersPerDay: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Max Transfers Per Pay Period</label>
                  <input
                    type="number"
                    min="1"
                    className="form-input"
                    value={limitsForm.maxTransfersPerPayPeriod}
                    onChange={(e) => setLimitsForm({ ...limitsForm, maxTransfersPerPayPeriod: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Max Amount Per Pay Period ($)</label>
                  <input
                    type="number"
                    step="0.01"
                    min="0"
                    className="form-input"
                    value={limitsForm.maxAmountPerPayPeriod}
                    onChange={(e) => setLimitsForm({ ...limitsForm, maxAmountPerPayPeriod: e.target.value })}
                    required
                  />
                </div>
                <div className="form-actions">
                  {customLimits && (
                    <button type="button" className="btn btn-danger" onClick={handleResetLimits}>
                      Reset to Defaults
                    </button>
                  )}
                  <button type="button" className="btn btn-secondary" onClick={() => setShowLimitsModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    Save Limits
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {showBankAccountModal && (
        <div className="modal-overlay" onClick={() => setShowBankAccountModal(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 className="modal-title">Add Bank Account</h3>
              <button className="modal-close" onClick={() => setShowBankAccountModal(false)}>
                <X />
              </button>
            </div>
            <div className="modal-body">
              <form onSubmit={handleSaveBankAccount}>
                <div className="form-group">
                  <label className="form-label">Bank Name</label>
                  <input
                    type="text"
                    className="form-input"
                    value={bankAccountForm.bankName}
                    onChange={(e) => setBankAccountForm({ ...bankAccountForm, bankName: e.target.value })}
                    required
                  />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Account Number (last 4)</label>
                    <input
                      type="text"
                      className="form-input"
                      maxLength={4}
                      value={bankAccountForm.accountNumberMasked}
                      onChange={(e) => setBankAccountForm({ ...bankAccountForm, accountNumberMasked: e.target.value })}
                      required
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Routing Number</label>
                    <input
                      type="text"
                      className="form-input"
                      value={bankAccountForm.routingNumber}
                      onChange={(e) => setBankAccountForm({ ...bankAccountForm, routingNumber: e.target.value })}
                      required
                    />
                  </div>
                </div>
                <div className="form-group">
                  <label className="form-label">Account Type</label>
                  <select
                    className="form-select"
                    value={bankAccountForm.accountType}
                    onChange={(e) => setBankAccountForm({ ...bankAccountForm, accountType: parseInt(e.target.value) })}
                  >
                    <option value={1}>Checking</option>
                    <option value={2}>Savings</option>
                  </select>
                </div>
                <div className="form-actions">
                  <button type="button" className="btn btn-secondary" onClick={() => setShowBankAccountModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-primary">
                    Add Bank Account
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
