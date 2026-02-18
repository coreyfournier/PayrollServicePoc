import { useQuery, useMutation } from 'urql';
import { GET_ALL_EMPLOYEES, DELETE_ALL_EMPLOYEES } from '../graphql/queries';
import { useState } from 'react';

function PayDetailModal({ employee, onClose }) {
  const pa = employee.payAttributes;
  if (!pa) return null;

  const formatCurrency = (val) => `$${Number(val).toFixed(2)}`;
  const payTypeLabel = pa.payType === '2' || pa.payType === 'Salary' ? 'Salary' : 'Hourly';

  return (
    <div className="confirm-modal-overlay" onClick={onClose}>
      <div className="pay-detail-modal" onClick={(e) => e.stopPropagation()}>
        <div className="pay-detail-header">
          <h3>Pay Details - {employee.firstName} {employee.lastName}</h3>
          <button className="btn btn-secondary" onClick={onClose}>Close</button>
        </div>
        <div className="pay-detail-period">
          Pay Period: {pa.payPeriodStart} to {pa.payPeriodEnd}
        </div>
        <div className="pay-detail-grid">
          <div className="pay-detail-label">Pay Rate</div>
          <div className="pay-detail-value">{formatCurrency(pa.payRate)} ({payTypeLabel})</div>

          <div className="pay-detail-label">Hours Worked</div>
          <div className="pay-detail-value">{Number(pa.totalHoursWorked).toFixed(2)}</div>

          <div className="pay-detail-label">Gross Pay</div>
          <div className="pay-detail-value pay-detail-gross">{formatCurrency(pa.grossPay)}</div>

          <div className="pay-detail-separator" />

          <div className="pay-detail-label">Federal Tax</div>
          <div className="pay-detail-value pay-detail-deduction">-{formatCurrency(pa.federalTax)}</div>

          <div className="pay-detail-label">State Tax</div>
          <div className="pay-detail-value pay-detail-deduction">-{formatCurrency(pa.stateTax)}</div>

          <div className="pay-detail-label">Addl. Federal Withholding</div>
          <div className="pay-detail-value pay-detail-deduction">-{formatCurrency(pa.additionalFederalWithholding)}</div>

          <div className="pay-detail-label">Addl. State Withholding</div>
          <div className="pay-detail-value pay-detail-deduction">-{formatCurrency(pa.additionalStateWithholding)}</div>

          <div className="pay-detail-label"><strong>Total Tax</strong></div>
          <div className="pay-detail-value pay-detail-deduction"><strong>-{formatCurrency(pa.totalTax)}</strong></div>

          <div className="pay-detail-separator" />

          <div className="pay-detail-label">Fixed Deductions</div>
          <div className="pay-detail-value pay-detail-deduction">-{formatCurrency(pa.totalFixedDeductions)}</div>

          <div className="pay-detail-label">Percent Deductions</div>
          <div className="pay-detail-value pay-detail-deduction">-{formatCurrency(pa.totalPercentDeductions)}</div>

          <div className="pay-detail-label"><strong>Total Deductions</strong></div>
          <div className="pay-detail-value pay-detail-deduction"><strong>-{formatCurrency(pa.totalDeductions)}</strong></div>

          <div className="pay-detail-separator" />

          <div className="pay-detail-label pay-detail-net-label">Net Pay</div>
          <div className="pay-detail-value pay-detail-net">{formatCurrency(pa.netPay)}</div>
        </div>
      </div>
    </div>
  );
}

export default function EmployeeList() {
  const [result, reexecuteQuery] = useQuery({ query: GET_ALL_EMPLOYEES });
  const [deleteResult, deleteAllEmployees] = useMutation(DELETE_ALL_EMPLOYEES);
  const [showConfirm, setShowConfirm] = useState(false);
  const [selectedEmployee, setSelectedEmployee] = useState(null);

  const { data, fetching, error } = result;

  const handleDeleteAll = async () => {
    await deleteAllEmployees();
    setShowConfirm(false);
    reexecuteQuery({ requestPolicy: 'network-only' });
  };

  if (error) {
    return <div className="error">Error: {error.message}</div>;
  }

  const employees = data?.employees || [];

  return (
    <div className="employee-list-container">
      <div className="employee-list-header">
        <h2>Employee Records</h2>
        <div className="header-actions">
          <button
            className="btn btn-refresh"
            onClick={() => reexecuteQuery({ requestPolicy: 'network-only' })}
            disabled={fetching}
          >
            Refresh
          </button>
          <button
            className="btn btn-danger"
            onClick={() => setShowConfirm(true)}
            disabled={fetching || employees.length === 0}
          >
            Delete All Records
          </button>
        </div>
      </div>

      {showConfirm && (
        <div className="confirm-modal-overlay">
          <div className="confirm-modal">
            <h3>Confirm Delete All</h3>
            <p>Are you sure you want to delete all {employees.length} employee records? This action cannot be undone.</p>
            <div className="confirm-actions">
              <button className="btn btn-secondary" onClick={() => setShowConfirm(false)}>
                Cancel
              </button>
              <button
                className="btn btn-danger"
                onClick={handleDeleteAll}
                disabled={deleteResult.fetching}
              >
                {deleteResult.fetching ? 'Deleting...' : 'Delete All'}
              </button>
            </div>
          </div>
        </div>
      )}

      {selectedEmployee && (
        <PayDetailModal
          employee={selectedEmployee}
          onClose={() => setSelectedEmployee(null)}
        />
      )}

      {deleteResult.data && (
        <div className="success-message">
          {deleteResult.data.deleteAllEmployees.message}
        </div>
      )}

      {fetching && <div className="loading">Loading employees...</div>}

      {!fetching && employees.length === 0 && (
        <div className="empty-state">
          No employee records found.
        </div>
      )}

      {!fetching && employees.length > 0 && (
        <>
          <div className="record-count">
            Total Records: {employees.length}
          </div>
          <div className="employee-table-wrapper">
            <table className="employee-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Email</th>
                  <th>Pay Type</th>
                  <th>Pay Rate</th>
                  <th>Net Pay</th>
                  <th>Status</th>
                  <th>Last Event</th>
                  <th>Updated</th>
                </tr>
              </thead>
              <tbody>
                {employees.map((employee) => (
                  <tr key={employee.id} className={employee.isActive ? '' : 'inactive'}>
                    <td className="name-cell">
                      {employee.firstName} {employee.lastName}
                    </td>
                    <td>{employee.email}</td>
                    <td>{employee.payType}</td>
                    <td className="pay-rate">
                      ${employee.payRate?.toFixed(2) || '0.00'}
                      {employee.payType === 'Salary' || employee.payType === '2'
                        ? ` (${employee.payPeriodHours ?? 40} hrs/pp)`
                        : ''}
                    </td>
                    <td
                      className={`pay-rate net-pay-cell${employee.payAttributes ? ' clickable' : ''}`}
                      onClick={() => employee.payAttributes && setSelectedEmployee(employee)}
                    >
                      {employee.payAttributes
                        ? `$${Number(employee.payAttributes.netPay).toFixed(2)}`
                        : '\u2014'}
                    </td>
                    <td>
                      <span className={`status-badge ${employee.isActive ? 'active' : 'inactive'}`}>
                        {employee.isActive ? 'Active' : 'Inactive'}
                      </span>
                    </td>
                    <td>
                      <span className="event-type">{employee.lastEventType}</span>
                    </td>
                    <td className="timestamp">
                      {new Date(employee.updatedAt).toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
