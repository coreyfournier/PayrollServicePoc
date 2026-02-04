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

// Tax Information
export const getTaxInfo = (employeeId) => api.get(`/taxinformation/employee/${employeeId}`);
export const createTaxInfo = (data) => api.post('/taxinformation', data);
export const updateTaxInfo = (employeeId, data) => api.put(`/taxinformation/employee/${employeeId}`, data);

// Deductions
export const getDeductions = (employeeId) => api.get(`/deductions/employee/${employeeId}`);
export const createDeduction = (data) => api.post('/deductions', data);
export const updateDeduction = (id, data) => api.put(`/deductions/${id}`, data);
export const deleteDeduction = (id) => api.delete(`/deductions/${id}`);

export default api;
