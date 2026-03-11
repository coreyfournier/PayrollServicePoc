import axios from 'axios';

const API_BASE = '/api';

const api = axios.create({
  baseURL: API_BASE,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Employees
export const getEmployees = () => api.get('/employees');
export const getEmployee = (id) => api.get(`/employees/${id}`);
export const createEmployee = (data) => api.post('/employees', data);
export const updateEmployee = (id, data) => api.put(`/employees/${id}`, data);
export const deleteEmployee = (id) => api.delete(`/employees/${id}`);

// Time Entries
export const getTimeEntries = (employeeId) => api.get(`/timeentries/employee/${employeeId}`);
export const clockIn = (employeeId) => api.post(`/timeentries/clock-in/${employeeId}`);
export const clockOut = (employeeId) => api.post(`/timeentries/clock-out/${employeeId}`);
export const updateTimeEntry = (id, data) => api.put(`/timeentries/${id}`, data);

// Tax Information
export const getTaxInfo = (employeeId) => api.get(`/taxinformation/employee/${employeeId}`);
export const createTaxInfo = (data) => api.post('/taxinformation', data);
export const updateTaxInfo = (employeeId, data) => api.put(`/taxinformation/employee/${employeeId}`, data);

// Deductions
export const getDeductions = (employeeId) => api.get(`/deductions/employee/${employeeId}`);
export const createDeduction = (data) => api.post('/deductions', data);
export const updateDeduction = (id, data) => api.put(`/deductions/${id}`, data);
export const deleteDeduction = (id) => api.delete(`/deductions/${id}`);

// Transfers
export const getTransfers = (employeeId) => api.get(`/transfers/employee/${employeeId}`);
export const initiateTransfer = (data) => api.post('/transfers', data);
export const getTransferLimits = (employeeId, payPeriodNumber) => api.get(`/transfers/employee/${employeeId}/limits`, { params: { payPeriodNumber } });
export const acceptTransferBalanceChange = (transferId, accepted) => api.post(`/transfers/${transferId}/accept`, { accepted });

// Employee Transfer Limits (custom per-employee overrides)
export const getEmployeeTransferLimits = (employeeId) => api.get(`/transfers/employee/${employeeId}/custom-limits`);
export const setEmployeeTransferLimits = (employeeId, data) => api.put(`/transfers/employee/${employeeId}/custom-limits`, data);
export const deleteEmployeeTransferLimits = (employeeId) => api.delete(`/transfers/employee/${employeeId}/custom-limits`);

// Bank Accounts
export const getBankAccounts = (employeeId) => api.get(`/bankaccounts/employee/${employeeId}`);
export const createBankAccount = (data) => api.post('/bankaccounts', data);
export const updateBankAccount = (id, data) => api.put(`/bankaccounts/${id}`, data);

export default api;
