import { useState } from 'react';
import { RefreshCw, Search, CheckCircle, XCircle, Clock, AlertTriangle, ArrowRight } from 'lucide-react';
import { getTransferWorkflow, getTransfers } from '../api';
import { format } from 'date-fns';

const WORKFLOW_STEPS = [
  { key: 'validate', label: 'Validate', activity: 'ValidateTransferActivity' },
  { key: 'balance', label: 'Verify Balance', activity: 'VerifyBalanceActivity' },
  { key: 'awaiting', label: 'Awaiting Confirmation', activity: 'MarkAwaitingConfirmationActivity' },
  { key: 'processing', label: 'Processing', activity: 'UpdateTransferStatusActivity' },
  { key: 'bank', label: 'Bank Transfer', activity: 'ExecuteBankTransferActivity' },
  { key: 'complete', label: 'Complete', activity: 'CompleteTransferActivity' },
];

const STATUS_STYLES = {
  Running: { color: '#3b82f6', bg: '#eff6ff', icon: Clock, label: 'Running' },
  Completed: { color: '#22c55e', bg: '#f0fdf4', icon: CheckCircle, label: 'Completed' },
  Failed: { color: '#ef4444', bg: '#fef2f2', icon: XCircle, label: 'Failed' },
  Suspended: { color: '#f59e0b', bg: '#fffbeb', icon: AlertTriangle, label: 'Suspended' },
  Terminated: { color: '#ef4444', bg: '#fef2f2', icon: XCircle, label: 'Terminated' },
  Pending: { color: '#6b7280', bg: '#f9fafb', icon: Clock, label: 'Pending' },
};

function getStepStatus(transfer, workflow) {
  if (!transfer || !workflow) return {};

  const statusNum = typeof transfer.status === 'number' ? transfer.status : null;
  const statusStr = typeof transfer.status === 'string' ? transfer.status :
    ['', 'Initiated', 'Processing', 'Completed', 'Failed', 'AwaitingConfirmation'][statusNum];
  const runtimeStatus = workflow.runtimeStatus;

  const steps = {};

  // Validate always completes if we got past initiation
  steps.validate = statusNum >= 1 || statusStr !== 'Initiated' ? 'done' : 'active';

  // Balance check
  if (statusStr === 'AwaitingConfirmation') {
    steps.balance = 'done';
    steps.awaiting = 'active';
    steps.processing = 'pending';
    steps.bank = 'pending';
    steps.complete = 'pending';
  } else if (statusStr === 'Processing') {
    steps.balance = 'done';
    steps.awaiting = 'skipped';
    steps.processing = 'done';
    steps.bank = 'active';
    steps.complete = 'pending';
  } else if (statusStr === 'Completed') {
    steps.balance = 'done';
    steps.awaiting = transfer.currentBalance != null ? 'done' : 'skipped';
    steps.processing = 'done';
    steps.bank = 'done';
    steps.complete = 'done';
  } else if (statusStr === 'Failed') {
    steps.balance = 'done';
    const reason = transfer.failureReason || '';
    if (reason.includes('auto-cancelled') || reason.includes('balance')) {
      steps.awaiting = 'failed';
      steps.processing = 'pending';
      steps.bank = 'pending';
    } else if (reason.includes('Bank') || reason.includes('bank') || reason.includes('retries')) {
      steps.awaiting = transfer.currentBalance != null ? 'done' : 'skipped';
      steps.processing = 'done';
      steps.bank = 'failed';
    } else {
      steps.awaiting = 'skipped';
      steps.processing = 'pending';
      steps.bank = 'pending';
    }
    steps.complete = 'failed';
  } else {
    // Initiated — still early
    steps.balance = runtimeStatus === 'Running' ? 'active' : 'pending';
    steps.awaiting = 'pending';
    steps.processing = 'pending';
    steps.bank = 'pending';
    steps.complete = 'pending';
  }

  return steps;
}

const STEP_COLORS = {
  done: { bg: '#dcfce7', border: '#22c55e', text: '#15803d' },
  active: { bg: '#dbeafe', border: '#3b82f6', text: '#1d4ed8' },
  failed: { bg: '#fee2e2', border: '#ef4444', text: '#dc2626' },
  skipped: { bg: '#f1f5f9', border: '#cbd5e1', text: '#94a3b8' },
  pending: { bg: '#f9fafb', border: '#e5e7eb', text: '#9ca3af' },
};

function TransferWorkflowPanel() {
  const [transferId, setTransferId] = useState('');
  const [workflow, setWorkflow] = useState(null);
  const [transfer, setTransfer] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);

  const loadWorkflow = async (id) => {
    const lookupId = id || transferId;
    if (!lookupId.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const [wfRes, trRes] = await Promise.allSettled([
        getTransferWorkflow(lookupId),
        // Transfer endpoint uses employee-scoped GET, but we can try the direct ID endpoint
        (async () => {
          const res = await fetch(`/api/transfers/${lookupId}`);
          if (!res.ok) return null;
          return res.json();
        })(),
      ]);

      if (wfRes.status === 'fulfilled') {
        setWorkflow(wfRes.value.data);
      } else {
        setWorkflow(null);
      }

      if (trRes.status === 'fulfilled' && trRes.value) {
        setTransfer(trRes.value);
      } else {
        setTransfer(null);
      }

      if (wfRes.status === 'rejected' && trRes.status === 'rejected') {
        setError('Transfer not found. Check the ID and try again.');
      }
    } catch (err) {
      setError('Failed to load workflow state.');
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = (e) => {
    e.preventDefault();
    loadWorkflow();
  };

  const stepStatuses = getStepStatus(transfer, workflow);
  const statusInfo = workflow ? STATUS_STYLES[workflow.runtimeStatus] || STATUS_STYLES.Pending : null;

  return (
    <div className="card" style={{ marginTop: '24px' }}>
      <div style={{ padding: '20px 24px 0' }}>
        <h3 style={{ fontSize: '16px', fontWeight: '600', margin: '0 0 4px' }}>Transfer Workflow Inspector</h3>
        <p style={{ fontSize: '13px', color: '#64748b', margin: '0 0 16px' }}>
          View the state machine progress for any transfer
        </p>

        <form onSubmit={handleSubmit} style={{ display: 'flex', gap: '8px', marginBottom: '16px' }}>
          <input
            type="text"
            className="form-input"
            placeholder="Enter Transfer ID (GUID)"
            value={transferId}
            onChange={(e) => setTransferId(e.target.value)}
            style={{ flex: 1, fontFamily: 'monospace', fontSize: '13px' }}
          />
          <button type="submit" className="btn btn-primary btn-sm" disabled={loading || !transferId.trim()}>
            <Search /> Lookup
          </button>
          {workflow && (
            <button type="button" className="btn btn-secondary btn-sm" onClick={() => loadWorkflow()} disabled={loading}>
              <RefreshCw className={loading ? 'spin' : ''} /> Refresh
            </button>
          )}
        </form>
      </div>

      {error && (
        <div style={{ padding: '0 24px 16px' }}>
          <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', padding: '12px', color: '#dc2626', fontSize: '13px' }}>
            {error}
          </div>
        </div>
      )}

      {workflow && (
        <div style={{ padding: '0 24px 20px' }}>
          {/* Workflow Header */}
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px',
            background: statusInfo?.bg || '#f9fafb', border: `1px solid ${statusInfo?.color || '#e5e7eb'}`,
            borderRadius: '8px', padding: '12px 16px' }}>
            <div>
              <div style={{ fontSize: '11px', color: '#64748b', textTransform: 'uppercase', marginBottom: '2px' }}>Workflow Status</div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                {statusInfo && <statusInfo.icon size={18} color={statusInfo.color} />}
                <span style={{ fontSize: '16px', fontWeight: '600', color: statusInfo?.color || '#1e293b' }}>
                  {workflow.runtimeStatus}
                </span>
              </div>
            </div>
            <div style={{ textAlign: 'right', fontSize: '12px', color: '#64748b' }}>
              {workflow.createdAt && (
                <div>Started: {format(new Date(workflow.createdAt), 'MMM d, h:mm:ss a')}</div>
              )}
              {workflow.lastUpdatedAt && (
                <div>Updated: {format(new Date(workflow.lastUpdatedAt), 'MMM d, h:mm:ss a')}</div>
              )}
            </div>
          </div>

          {/* Step Pipeline */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '4px', overflowX: 'auto', paddingBottom: '8px' }}>
            {WORKFLOW_STEPS.map((step, i) => {
              const status = stepStatuses[step.key] || 'pending';
              const colors = STEP_COLORS[status];
              return (
                <div key={step.key} style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                  <div style={{
                    background: colors.bg,
                    border: `2px solid ${colors.border}`,
                    borderRadius: '8px',
                    padding: '8px 12px',
                    minWidth: '100px',
                    textAlign: 'center',
                    position: 'relative',
                  }}>
                    <div style={{ fontSize: '11px', fontWeight: '600', color: colors.text, textTransform: 'uppercase' }}>
                      {step.label}
                    </div>
                    <div style={{ fontSize: '10px', color: colors.text, marginTop: '2px', opacity: 0.8 }}>
                      {status === 'done' ? 'Completed' :
                       status === 'active' ? 'In Progress' :
                       status === 'failed' ? 'Failed' :
                       status === 'skipped' ? 'Skipped' : 'Pending'}
                    </div>
                  </div>
                  {i < WORKFLOW_STEPS.length - 1 && (
                    <ArrowRight size={14} color={status === 'done' ? '#22c55e' : '#d1d5db'} />
                  )}
                </div>
              );
            })}
          </div>

          {/* Transfer Details */}
          {transfer && (
            <div style={{ marginTop: '16px', display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '12px' }}>
              <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '10px 12px' }}>
                <div style={{ fontSize: '10px', color: '#94a3b8', textTransform: 'uppercase' }}>Amount</div>
                <div style={{ fontSize: '16px', fontWeight: '600' }}>${transfer.amount?.toFixed(2)}</div>
              </div>
              <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '10px 12px' }}>
                <div style={{ fontSize: '10px', color: '#94a3b8', textTransform: 'uppercase' }}>Pay Period</div>
                <div style={{ fontSize: '16px', fontWeight: '600' }}>{transfer.payPeriodNumber}</div>
              </div>
              {transfer.currentBalance != null && (
                <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '10px 12px' }}>
                  <div style={{ fontSize: '10px', color: '#94a3b8', textTransform: 'uppercase' }}>Balance at Check</div>
                  <div style={{ fontSize: '16px', fontWeight: '600' }}>${transfer.currentBalance?.toFixed(2)}</div>
                </div>
              )}
              {transfer.externalReferenceId && (
                <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '10px 12px' }}>
                  <div style={{ fontSize: '10px', color: '#94a3b8', textTransform: 'uppercase' }}>Bank Reference</div>
                  <div style={{ fontSize: '13px', fontWeight: '600', fontFamily: 'monospace' }}>{transfer.externalReferenceId}</div>
                </div>
              )}
              {transfer.failureReason && (
                <div style={{ background: '#fef2f2', borderRadius: '8px', padding: '10px 12px', gridColumn: 'span 2' }}>
                  <div style={{ fontSize: '10px', color: '#dc2626', textTransform: 'uppercase' }}>Failure Reason</div>
                  <div style={{ fontSize: '13px', color: '#dc2626' }}>{transfer.failureReason}</div>
                </div>
              )}
            </div>
          )}

          {/* Workflow Input/Output */}
          {workflow.serializedInput && (
            <details style={{ marginTop: '12px' }}>
              <summary style={{ fontSize: '12px', color: '#64748b', cursor: 'pointer' }}>Workflow Input/Output (raw)</summary>
              <pre style={{ fontSize: '11px', background: '#f1f5f9', padding: '10px', borderRadius: '6px', overflow: 'auto', maxHeight: '200px', marginTop: '6px' }}>
                {JSON.stringify(workflow.serializedInput, null, 2)}
              </pre>
              {workflow.serializedOutput && (
                <pre style={{ fontSize: '11px', background: '#f1f5f9', padding: '10px', borderRadius: '6px', overflow: 'auto', maxHeight: '200px', marginTop: '6px' }}>
                  {JSON.stringify(workflow.serializedOutput, null, 2)}
                </pre>
              )}
            </details>
          )}
        </div>
      )}
    </div>
  );
}

export default TransferWorkflowPanel;
