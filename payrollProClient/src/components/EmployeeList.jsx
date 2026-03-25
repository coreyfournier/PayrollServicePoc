import { useQuery, useMutation, useSubscription } from 'urql';
import { GET_ALL_EMPLOYEES, GET_ALL_TRANSFERS, DELETE_ALL_EMPLOYEES } from '../graphql/queries';
import { EMPLOYEE_CHANGE_SUBSCRIPTION, TRANSFER_CHANGE_SUBSCRIPTION } from '../graphql/subscriptions';
import { useState, useEffect, useRef, useCallback } from 'react';
import TransferPanel from './TransferPanel';

function PayDetailModal({ employee, transfers, onClose, onTransfer }) {
  const pa = employee.payAttributes;
  if (!pa) return null;

  const formatCurrency = (val) => `$${Number(val).toFixed(2)}`;
  const payTypeLabel = pa.payType === '2' || pa.payType === 'Salary' ? 'Salary' : 'Hourly';
  const period = String(pa.payPeriodNumber);
  const transferredAmount = (transfers || [])
    .filter(t => t.status !== 'Failed' && String(t.payPeriodNumber) === period)
    .reduce((sum, t) => sum + Number(t.amount), 0);
  const availableBalance = Number(pa.netPay) - transferredAmount;

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

          {transferredAmount > 0 && (
            <>
              <div className="pay-detail-label">Transfers</div>
              <div className="pay-detail-value pay-detail-deduction">-{formatCurrency(transferredAmount)}</div>
            </>
          )}

          <div className="pay-detail-separator" />

          <div className="pay-detail-label pay-detail-net-label">Available Balance</div>
          <div className="pay-detail-value pay-detail-net">{formatCurrency(availableBalance)}</div>
        </div>
        <div className="pay-detail-actions">
          <button className="btn btn-primary" onClick={onTransfer}>
            Transfer
          </button>
        </div>
      </div>
    </div>
  );
}

export default function EmployeeList() {
  const [result, reexecuteQuery] = useQuery({ query: GET_ALL_EMPLOYEES });
  const [deleteResult, deleteAllEmployees] = useMutation(DELETE_ALL_EMPLOYEES);
  const [showConfirm, setShowConfirm] = useState(false);
  const [selectedEmployeeId, setSelectedEmployeeId] = useState(null);
  const [view, setView] = useState('pay'); // 'pay' or 'transfer'
  const [transferFromPay, setTransferFromPay] = useState(false);

  const [employees, setEmployees] = useState([]);
  const [transferMap, setTransferMap] = useState({});
  const [highlightedIds, setHighlightedIds] = useState(new Map());
  const highlightTimers = useRef(new Map());
  const queryInitialized = useRef(false);

  const { data, fetching, error } = result;
  const [transfersResult] = useQuery({ query: GET_ALL_TRANSFERS });

  const [subResult] = useSubscription({ query: EMPLOYEE_CHANGE_SUBSCRIPTION });
  const [transferSubResult] = useSubscription({ query: TRANSFER_CHANGE_SUBSCRIPTION });

  const triggerHighlight = useCallback((id, changeType) => {
    setHighlightedIds(prev => {
      const next = new Map(prev);
      next.set(id, changeType);
      return next;
    });

    if (highlightTimers.current.has(id)) {
      clearTimeout(highlightTimers.current.get(id));
    }

    const timer = setTimeout(() => {
      setHighlightedIds(prev => {
        const next = new Map(prev);
        next.delete(id);
        return next;
      });
      highlightTimers.current.delete(id);
    }, 2000);
    highlightTimers.current.set(id, timer);
  }, []);

  // Cleanup highlight timers on unmount
  useEffect(() => {
    return () => {
      highlightTimers.current.forEach(timer => clearTimeout(timer));
    };
  }, []);

  // Seed employees from query result
  useEffect(() => {
    if (data?.employees) {
      setEmployees(data.employees);
      queryInitialized.current = true;
    }
  }, [data]);

  // Merge subscription events into local state
  useEffect(() => {
    if (!subResult.data?.onEmployeeChanged || !queryInitialized.current) return;

    const { employee: incoming, changeType } = subResult.data.onEmployeeChanged;

    setEmployees(prev => {
      const idx = prev.findIndex(e => e.id === incoming.id);

      if (idx >= 0) {
        // Update existing employee, preserving fields the subscription doesn't include (e.g. createdAt)
        const updated = [...prev];
        updated[idx] = { ...prev[idx], ...incoming };
        return updated;
      } else {
        // New employee — prepend
        return [incoming, ...prev];
      }
    });

    triggerHighlight(incoming.id, changeType);
  }, [subResult.data, triggerHighlight]);

  // Build transfer map from query result
  useEffect(() => {
    if (!transfersResult.data?.transfers) return;
    const map = {};
    for (const t of transfersResult.data.transfers) {
      if (!map[t.employeeId]) map[t.employeeId] = [];
      map[t.employeeId].push(t);
    }
    setTransferMap(map);
  }, [transfersResult.data]);

  // Merge transfer subscription events into map
  useEffect(() => {
    if (!transferSubResult.data?.onTransferChanged) return;
    const { transfer: incoming } = transferSubResult.data.onTransferChanged;

    setTransferMap(prev => {
      const list = [...(prev[incoming.employeeId] || [])];
      const idx = list.findIndex(t => t.id === incoming.id);
      if (idx >= 0) {
        list[idx] = { ...list[idx], ...incoming };
      } else {
        list.unshift(incoming);
      }
      return { ...prev, [incoming.employeeId]: list };
    });

    triggerHighlight(incoming.employeeId, 'transferUpdate');
  }, [transferSubResult.data, triggerHighlight]);

  const handleDeleteAll = async () => {
    await deleteAllEmployees();
    setShowConfirm(false);
    setHighlightedIds(new Map());
    highlightTimers.current.forEach(timer => clearTimeout(timer));
    highlightTimers.current.clear();
    reexecuteQuery({ requestPolicy: 'network-only' });
  };

  const getHighlightClass = (id) => {
    const changeType = highlightedIds.get(id);
    if (!changeType) return '';
    switch (changeType) {
      case 'created':
      case 'activated':
        return 'row-highlight-green';
      case 'updated':
        return 'row-highlight-blue';
      case 'deactivated':
        return 'row-highlight-red';
      case 'payUpdated':
        return 'row-highlight-amber';
      case 'transferUpdate':
        return 'row-highlight-blue';
      default:
        return 'row-highlight-blue';
    }
  };

  const TERMINAL = ['Completed', 'Failed'];
  const getTransferIndicator = (employeeId) => {
    const list = transferMap[employeeId] || [];
    const active = list.filter(t => !TERMINAL.includes(t.status));
    const awaiting = active.filter(t => t.status === 'AwaitingConfirmation');
    return { total: list.length, active: active.length, awaiting: awaiting.length };
  };

  // Derive selected employee from live state so the modal reflects updates
  const selectedEmployee = selectedEmployeeId
    ? employees.find(e => e.id === selectedEmployeeId)
    : null;

  if (error) {
    return <div className="error">Error: {error.message}</div>;
  }

  return (
    <div className="employee-list-container">
      <div className="employee-list-header">
        <div className="employee-list-title">
          <h2>Employee Records</h2>
          <span className={`live-indicator ${subResult.fetching ? 'connecting' : 'connected'}`}>
            <span className="live-dot" />
            {subResult.fetching ? 'Connecting' : 'Live'}
          </span>
        </div>
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

      {selectedEmployee && view === 'pay' && (
        <PayDetailModal
          employee={selectedEmployee}
          transfers={transferMap[selectedEmployeeId] || []}
          onClose={() => { setSelectedEmployeeId(null); setView('pay'); }}
          onTransfer={() => { setTransferFromPay(true); setView('transfer'); }}
        />
      )}

      {selectedEmployee && view === 'transfer' && (
        <TransferPanel
          employee={selectedEmployee}
          onClose={() => { setSelectedEmployeeId(null); setView('pay'); }}
          onBack={transferFromPay ? () => setView('pay') : null}
        />
      )}

      {deleteResult.data && (
        <div className="success-message">
          {deleteResult.data.deleteAllEmployees.message}
        </div>
      )}

      {fetching && employees.length === 0 && <div className="loading">Loading employees...</div>}

      {!fetching && employees.length === 0 && (
        <div className="empty-state">
          No employee records found.
        </div>
      )}

      {employees.length > 0 && (
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
                  <th>Available</th>
                  <th>Transfers</th>
                  <th>Status</th>
                  <th>Last Event</th>
                  <th>Updated</th>
                </tr>
              </thead>
              <tbody>
                {employees.map((employee) => (
                  <tr
                    key={employee.id}
                    className={`${employee.isActive ? '' : 'inactive'} ${getHighlightClass(employee.id)}`}
                  >
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
                      onClick={() => employee.payAttributes && setSelectedEmployeeId(employee.id)}
                    >
                      {employee.payAttributes
                        ? (() => {
                            const netPay = Number(employee.payAttributes.netPay);
                            const period = String(employee.payAttributes.payPeriodNumber);
                            const transferred = (transferMap[employee.id] || [])
                              .filter(t => t.status !== 'Failed' && String(t.payPeriodNumber) === period)
                              .reduce((sum, t) => sum + Number(t.amount), 0);
                            return `$${(netPay - transferred).toFixed(2)}`;
                          })()
                        : '\u2014'}
                    </td>
                    <td className="transfer-indicator-cell">
                      {(() => {
                        const ti = getTransferIndicator(employee.id);
                        if (ti.total === 0) return <span className="transfer-indicator-none">&mdash;</span>;
                        const openTransfers = (e) => {
                          e.stopPropagation();
                          setSelectedEmployeeId(employee.id);
                          setTransferFromPay(false);
                          setView('transfer');
                        };
                        return (
                          <span className="transfer-indicator" onClick={openTransfers} style={{ cursor: 'pointer' }}>
                            {ti.awaiting > 0 && (
                              <span className="transfer-badge-awaiting" title={`${ti.awaiting} awaiting confirmation — click to inspect`}>
                                &#9888; {ti.awaiting}
                              </span>
                            )}
                            {ti.active > 0 && ti.awaiting === 0 && (
                              <span className="transfer-badge-active" title={`${ti.active} in progress — click to inspect`}>
                                {ti.active} active
                              </span>
                            )}
                            {ti.active === 0 && (
                              <span className="transfer-badge-done" title={`${ti.total} total transfers — click to inspect`}>
                                {ti.total} total
                              </span>
                            )}
                          </span>
                        );
                      })()}
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
