import { useState, useEffect, useCallback } from 'react';
import { ArrowRightLeft, RefreshCw, Filter, Search, CheckCircle, XCircle, Clock, AlertTriangle, ArrowRight } from 'lucide-react';
import { getRecentTransfers, getTransferWorkflow, acceptTransferBalanceChange, getEmployees } from '../api';
import { format } from 'date-fns';

const STATUS_OPTIONS = [
  { value: '', label: 'All Statuses' },
  { value: 'Initiated', label: 'Initiated' },
  { value: 'Processing', label: 'Processing' },
  { value: 'Completed', label: 'Completed' },
  { value: 'Failed', label: 'Failed' },
  { value: 'AwaitingConfirmation', label: 'Awaiting Confirmation' },
];

const STATUS_BADGES = {
  0: { label: 'Initiated', className: 'badge-info' },
  1: { label: 'Initiated', className: 'badge-info' },
  2: { label: 'Processing', className: 'badge-warning' },
  3: { label: 'Completed', className: 'badge-success' },
  4: { label: 'Failed', className: 'badge-danger' },
  5: { label: 'Awaiting Confirmation', className: 'badge-warning' },
  Initiated: { label: 'Initiated', className: 'badge-info' },
  Processing: { label: 'Processing', className: 'badge-warning' },
  Completed: { label: 'Completed', className: 'badge-success' },
  Failed: { label: 'Failed', className: 'badge-danger' },
  AwaitingConfirmation: { label: 'Awaiting Confirmation', className: 'badge-warning' },
};

const WORKFLOW_STEPS = [
  { key: 'validate', label: 'Validate' },
  { key: 'balance', label: 'Verify Balance' },
  { key: 'awaiting', label: 'Awaiting Confirmation' },
  { key: 'processing', label: 'Processing' },
  { key: 'bank', label: 'Bank Transfer' },
  { key: 'complete', label: 'Complete' },
];

const WF_STATUS_STYLES = {
  Running: { color: '#3b82f6', bg: '#eff6ff', icon: Clock, label: 'Running' },
  Completed: { color: '#22c55e', bg: '#f0fdf4', icon: CheckCircle, label: 'Completed' },
  Failed: { color: '#ef4444', bg: '#fef2f2', icon: XCircle, label: 'Failed' },
  Suspended: { color: '#f59e0b', bg: '#fffbeb', icon: AlertTriangle, label: 'Suspended' },
  Terminated: { color: '#ef4444', bg: '#fef2f2', icon: XCircle, label: 'Terminated' },
  Pending: { color: '#6b7280', bg: '#f9fafb', icon: Clock, label: 'Pending' },
};

const STEP_COLORS = {
  done: { bg: '#dcfce7', border: '#22c55e', text: '#15803d' },
  active: { bg: '#dbeafe', border: '#3b82f6', text: '#1d4ed8' },
  failed: { bg: '#fee2e2', border: '#ef4444', text: '#dc2626' },
  skipped: { bg: '#f1f5f9', border: '#cbd5e1', text: '#94a3b8' },
  pending: { bg: '#f9fafb', border: '#e5e7eb', text: '#9ca3af' },
};

function getStatusString(status) {
  if (typeof status === 'string') return status;
  return ['', 'Initiated', 'Processing', 'Completed', 'Failed', 'AwaitingConfirmation'][status] || '';
}

function getStepStatus(transfer, workflow) {
  if (!transfer || !workflow) return {};
  const statusStr = getStatusString(transfer.status);
  const runtimeStatus = workflow.runtimeStatus;
  const steps = {};

  steps.validate = statusStr !== 'Initiated' || runtimeStatus === 'Running' ? 'done' : 'active';

  if (statusStr === 'AwaitingConfirmation') {
    steps.balance = 'done'; steps.awaiting = 'active'; steps.processing = 'pending'; steps.bank = 'pending'; steps.complete = 'pending';
  } else if (statusStr === 'Processing') {
    steps.balance = 'done'; steps.awaiting = 'skipped'; steps.processing = 'done'; steps.bank = 'active'; steps.complete = 'pending';
  } else if (statusStr === 'Completed') {
    steps.balance = 'done'; steps.awaiting = transfer.currentBalance != null ? 'done' : 'skipped';
    steps.processing = 'done'; steps.bank = 'done'; steps.complete = 'done';
  } else if (statusStr === 'Failed') {
    steps.balance = 'done';
    const reason = transfer.failureReason || '';
    if (reason.includes('auto-cancelled') || reason.includes('balance')) {
      steps.awaiting = 'failed'; steps.processing = 'pending'; steps.bank = 'pending';
    } else if (reason.includes('Bank') || reason.includes('bank') || reason.includes('retries')) {
      steps.awaiting = transfer.currentBalance != null ? 'done' : 'skipped'; steps.processing = 'done'; steps.bank = 'failed';
    } else {
      steps.awaiting = 'skipped'; steps.processing = 'pending'; steps.bank = 'pending';
    }
    steps.complete = 'failed';
  } else {
    steps.balance = runtimeStatus === 'Running' ? 'active' : 'pending';
    steps.awaiting = 'pending'; steps.processing = 'pending'; steps.bank = 'pending'; steps.complete = 'pending';
  }
  return steps;
}

function Transfers() {
  const [transfers, setTransfers] = useState([]);
  const [employeeMap, setEmployeeMap] = useState({});
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('');
  const [selectedTransfer, setSelectedTransfer] = useState(null);
  const [workflow, setWorkflow] = useState(null);
  const [workflowLoading, setWorkflowLoading] = useState(false);

  useEffect(() => {
    getEmployees().then(res => {
      const map = {};
      res.data.forEach(e => { map[e.id] = `${e.firstName} ${e.lastName}`; });
      setEmployeeMap(map);
    }).catch(() => {});
  }, []);

  const loadTransfers = useCallback(async () => {
    setLoading(true);
    try {
      const res = await getRecentTransfers(50, statusFilter || null);
      setTransfers(res.data);
    } catch (err) {
      console.error('Error loading transfers:', err);
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => { loadTransfers(); }, [loadTransfers]);

  const loadWorkflow = async (transfer) => {
    setSelectedTransfer(transfer);
    setWorkflowLoading(true);
    setWorkflow(null);
    try {
      const res = await getTransferWorkflow(transfer.id);
      setWorkflow(res.data);
    } catch {
      setWorkflow(null);
    } finally {
      setWorkflowLoading(false);
    }
  };

  const refreshWorkflow = async () => {
    if (!selectedTransfer) return;
    setWorkflowLoading(true);
    try {
      const [wfRes, trRes] = await Promise.allSettled([
        getTransferWorkflow(selectedTransfer.id),
        getRecentTransfers(50, statusFilter || null),
      ]);
      if (wfRes.status === 'fulfilled') setWorkflow(wfRes.value.data);
      if (trRes.status === 'fulfilled') {
        setTransfers(trRes.value.data);
        const updated = trRes.value.data.find(t => t.id === selectedTransfer.id);
        if (updated) setSelectedTransfer(updated);
      }
    } catch { /* ignore refresh errors */ }
    finally { setWorkflowLoading(false); }
  };

  const handleAccept = async (transferId, accepted) => {
    try {
      await acceptTransferBalanceChange(transferId, accepted);
      await refreshWorkflow();
    } catch (err) {
      console.error('Error accepting/rejecting transfer:', err);
    }
  };

  const statusBadge = (status) => {
    const info = STATUS_BADGES[status] || { label: String(status), className: 'badge-secondary' };
    return <span className={`badge ${info.className}`}>{info.label}</span>;
  };

  const stepStatuses = getStepStatus(selectedTransfer, workflow);
  const statusInfo = workflow ? WF_STATUS_STYLES[workflow.runtimeStatus] || WF_STATUS_STYLES.Pending : null;
  const statusStr = selectedTransfer ? getStatusString(selectedTransfer.status) : '';

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Transfers</h1>
          <p className="page-subtitle">Monitor and inspect transfer workflows</p>
        </div>
        <button className="btn btn-secondary" onClick={loadTransfers} disabled={loading}>
          <RefreshCw className={loading ? 'spin' : ''} /> Refresh
        </button>
      </div>

      {/* Filter Bar */}
      <div className="card" style={{ marginBottom: '24px' }}>
        <div style={{ padding: '16px 24px', display: 'flex', alignItems: 'center', gap: '12px' }}>
          <Filter size={18} style={{ color: 'var(--text-muted)' }} />
          <select
            className="form-select"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            style={{ width: '220px' }}
          >
            {STATUS_OPTIONS.map(opt => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          <span style={{ fontSize: '13px', color: 'var(--text-secondary)' }}>
            {transfers.length} transfer{transfers.length !== 1 ? 's' : ''}
          </span>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: selectedTransfer ? '1fr 1fr' : '1fr', gap: '24px' }}>
        {/* Transfer List - grouped by employee */}
        <div>
          {loading && (
            <div className="card"><div className="loading"><div className="spinner"></div></div></div>
          )}
          {!loading && transfers.length === 0 && (
            <div className="card">
              <div className="empty-state">
                <ArrowRightLeft />
                <h3>No transfers found</h3>
                <p>Try adjusting your filter or initiate a transfer from an employee's detail page.</p>
              </div>
            </div>
          )}
          {!loading && transfers.length > 0 && (() => {
            const grouped = {};
            transfers.forEach(t => {
              const key = t.employeeId;
              if (!grouped[key]) grouped[key] = [];
              grouped[key].push(t);
            });
            // Sort groups by most recent transfer
            const sortedGroups = Object.entries(grouped).sort((a, b) => {
              const latestA = Math.max(...a[1].map(t => new Date(t.initiatedAt).getTime()));
              const latestB = Math.max(...b[1].map(t => new Date(t.initiatedAt).getTime()));
              return latestB - latestA;
            });
            return sortedGroups.map(([employeeId, empTransfers]) => (
              <div key={employeeId} className="card" style={{ marginBottom: '16px' }}>
                <div style={{
                  padding: '12px 24px', borderBottom: '1px solid var(--border)',
                  display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                  background: 'var(--background)'
                }}>
                  <strong style={{ fontSize: '14px' }}>{employeeMap[employeeId] || 'Unknown'}</strong>
                  <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
                    {empTransfers.length} transfer{empTransfers.length !== 1 ? 's' : ''}
                  </span>
                </div>
                <div className="table-container">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Date</th>
                        <th>Amount</th>
                        <th>Status</th>
                        <th>Period</th>
                      </tr>
                    </thead>
                    <tbody>
                      {empTransfers
                        .sort((a, b) => new Date(b.initiatedAt) - new Date(a.initiatedAt))
                        .map(t => (
                        <tr
                          key={t.id}
                          className="clickable"
                          onClick={() => loadWorkflow(t)}
                          style={selectedTransfer?.id === t.id ? { background: 'var(--surface-hover)' } : undefined}
                        >
                          <td style={{ fontSize: '13px' }}>{format(new Date(t.initiatedAt), 'MMM d, h:mm a')}</td>
                          <td style={{ fontWeight: '600' }}>${t.amount?.toFixed(2)}</td>
                          <td>{statusBadge(t.status)}</td>
                          <td style={{ fontSize: '13px', color: 'var(--text-secondary)' }}>#{t.payPeriodNumber}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            ));
          })()}
        </div>

        {/* Workflow Inspector Panel */}
        {selectedTransfer && (
          <div className="card">
            <div className="card-header">
              <h3 className="card-title">Workflow Inspector</h3>
              <button className="btn btn-secondary btn-sm" onClick={refreshWorkflow} disabled={workflowLoading}>
                <RefreshCw size={14} className={workflowLoading ? 'spin' : ''} /> Refresh
              </button>
            </div>
            <div className="card-body">
              {/* Employee Name */}
              {employeeMap[selectedTransfer.employeeId] && (
                <div style={{ fontSize: '15px', fontWeight: '600', marginBottom: '16px' }}>
                  {employeeMap[selectedTransfer.employeeId]}
                </div>
              )}
              {/* Transfer Summary */}
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '20px' }}>
                <div style={{ background: 'var(--background)', borderRadius: '8px', padding: '10px 12px' }}>
                  <div style={{ fontSize: '10px', color: 'var(--text-muted)', textTransform: 'uppercase' }}>Amount</div>
                  <div style={{ fontSize: '18px', fontWeight: '600' }}>${selectedTransfer.amount?.toFixed(2)}</div>
                </div>
                <div style={{ background: 'var(--background)', borderRadius: '8px', padding: '10px 12px' }}>
                  <div style={{ fontSize: '10px', color: 'var(--text-muted)', textTransform: 'uppercase' }}>Pay Period</div>
                  <div style={{ fontSize: '18px', fontWeight: '600' }}>#{selectedTransfer.payPeriodNumber}</div>
                </div>
                <div style={{ background: 'var(--background)', borderRadius: '8px', padding: '10px 12px' }}>
                  <div style={{ fontSize: '10px', color: 'var(--text-muted)', textTransform: 'uppercase' }}>Status</div>
                  <div style={{ marginTop: '4px' }}>{statusBadge(selectedTransfer.status)}</div>
                </div>
                <div style={{ background: 'var(--background)', borderRadius: '8px', padding: '10px 12px' }}>
                  <div style={{ fontSize: '10px', color: 'var(--text-muted)', textTransform: 'uppercase' }}>Transfer ID</div>
                  <div style={{ fontSize: '11px', fontWeight: '600', fontFamily: 'monospace', wordBreak: 'break-all' }}>{selectedTransfer.id}</div>
                </div>
              </div>

              {/* Accept/Reject for AwaitingConfirmation */}
              {statusStr === 'AwaitingConfirmation' && (
                <div style={{ background: '#fffbeb', border: '1px solid #fbbf24', borderRadius: '8px', padding: '12px 16px', marginBottom: '20px' }}>
                  <div style={{ fontSize: '13px', fontWeight: '600', color: '#92400e', marginBottom: '4px' }}>
                    Balance confirmation required
                  </div>
                  {selectedTransfer.currentBalance != null && (
                    <div style={{ fontSize: '12px', color: '#92400e', marginBottom: '8px' }}>
                      Current balance: ${selectedTransfer.currentBalance.toFixed(2)} (transfer: ${selectedTransfer.amount.toFixed(2)})
                    </div>
                  )}
                  <div style={{ display: 'flex', gap: '8px' }}>
                    <button className="btn btn-success btn-sm" onClick={() => handleAccept(selectedTransfer.id, true)}>
                      <CheckCircle size={14} /> Accept
                    </button>
                    <button className="btn btn-danger btn-sm" onClick={() => handleAccept(selectedTransfer.id, false)}>
                      <XCircle size={14} /> Reject
                    </button>
                  </div>
                </div>
              )}

              {/* Workflow Status & Pipeline */}
              {workflowLoading && (
                <div className="loading"><div className="spinner"></div></div>
              )}

              {!workflowLoading && workflow && (
                <>
                  {/* Workflow Header */}
                  <div style={{
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px',
                    background: statusInfo?.bg || '#f9fafb', border: `1px solid ${statusInfo?.color || '#e5e7eb'}`,
                    borderRadius: '8px', padding: '10px 14px'
                  }}>
                    <div>
                      <div style={{ fontSize: '10px', color: 'var(--text-secondary)', textTransform: 'uppercase', marginBottom: '2px' }}>Workflow</div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                        {statusInfo && <statusInfo.icon size={16} color={statusInfo.color} />}
                        <span style={{ fontSize: '14px', fontWeight: '600', color: statusInfo?.color }}>{workflow.runtimeStatus}</span>
                      </div>
                    </div>
                    <div style={{ textAlign: 'right', fontSize: '11px', color: 'var(--text-secondary)' }}>
                      {workflow.createdAt && <div>Started: {format(new Date(workflow.createdAt), 'MMM d, h:mm:ss a')}</div>}
                      {workflow.lastUpdatedAt && <div>Updated: {format(new Date(workflow.lastUpdatedAt), 'MMM d, h:mm:ss a')}</div>}
                    </div>
                  </div>

                  {/* Step Pipeline - vertical for side panel */}
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    {WORKFLOW_STEPS.map((step) => {
                      const status = stepStatuses[step.key] || 'pending';
                      const colors = STEP_COLORS[status];
                      return (
                        <div key={step.key} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <div style={{
                            background: colors.bg, border: `2px solid ${colors.border}`,
                            borderRadius: '8px', padding: '8px 12px', flex: 1,
                            display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                          }}>
                            <span style={{ fontSize: '12px', fontWeight: '600', color: colors.text }}>{step.label}</span>
                            <span style={{ fontSize: '10px', color: colors.text, opacity: 0.8 }}>
                              {status === 'done' ? 'Completed' : status === 'active' ? 'In Progress' :
                               status === 'failed' ? 'Failed' : status === 'skipped' ? 'Skipped' : 'Pending'}
                            </span>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </>
              )}

              {!workflowLoading && !workflow && (
                <div style={{ textAlign: 'center', padding: '24px', color: 'var(--text-secondary)', fontSize: '13px' }}>
                  Workflow data not available. The transfer may still be queued.
                </div>
              )}

              {/* Extra details */}
              {selectedTransfer.externalReferenceId && (
                <div style={{ background: 'var(--background)', borderRadius: '8px', padding: '10px 12px', marginTop: '12px' }}>
                  <div style={{ fontSize: '10px', color: 'var(--text-muted)', textTransform: 'uppercase' }}>Bank Reference</div>
                  <div style={{ fontSize: '12px', fontWeight: '600', fontFamily: 'monospace' }}>{selectedTransfer.externalReferenceId}</div>
                </div>
              )}
              {selectedTransfer.failureReason && (
                <div style={{ background: '#fef2f2', borderRadius: '8px', padding: '10px 12px', marginTop: '12px' }}>
                  <div style={{ fontSize: '10px', color: '#dc2626', textTransform: 'uppercase' }}>Failure Reason</div>
                  <div style={{ fontSize: '12px', color: '#dc2626' }}>{selectedTransfer.failureReason}</div>
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </>
  );
}

export default Transfers;
