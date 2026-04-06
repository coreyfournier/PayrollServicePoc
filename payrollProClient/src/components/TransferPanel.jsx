import { useQuery, useSubscription } from 'urql';
import { GET_TRANSFERS_BY_EMPLOYEE } from '../graphql/queries';
import { TRANSFER_CHANGE_SUBSCRIPTION, TRANSFER_STATUS_SUBSCRIPTION } from '../graphql/subscriptions';
import { useState, useEffect, useCallback } from 'react';

export default function TransferPanel({ employee, onClose, onBack }) {
  const pa = employee.payAttributes;
  const netPay = pa ? Number(pa.netPay) : 0;
  const payPeriod = pa?.payPeriodNumber ? String(pa.payPeriodNumber) : '';

  const [transfers, setTransfers] = useState([]);

  const availableBalance = netPay - Number(pa?.transferTotalAmount || 0);
  const [canTransfer, setCanTransfer] = useState(true);
  const [transferLimits, setTransferLimits] = useState(null);
  const [bankAccounts, setBankAccounts] = useState([]);
  const [loadingAccounts, setLoadingAccounts] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(null);
  const [form, setForm] = useState({ bankAccountId: '' });
  const [selectedAmount, setSelectedAmount] = useState(null);
  const [customAmount, setCustomAmount] = useState('');
  const presetAmounts = [50, 100, 150].filter(a => a <= availableBalance);

  const [transferResult] = useQuery({
    query: GET_TRANSFERS_BY_EMPLOYEE,
    variables: { employeeId: employee.id },
  });

  const [subResult] = useSubscription({ query: TRANSFER_CHANGE_SUBSCRIPTION });
  const [statusSubResult] = useSubscription({ query: TRANSFER_STATUS_SUBSCRIPTION });

  // Seed transfers from query
  useEffect(() => {
    if (transferResult.data?.transfersByEmployeeId) {
      setTransfers(transferResult.data.transfersByEmployeeId);
    }
  }, [transferResult.data]);

  // Merge subscription updates
  useEffect(() => {
    if (!subResult.data?.onTransferChanged) return;
    const { transfer: incoming } = subResult.data.onTransferChanged;
    if (incoming.employeeId !== employee.id) return;

    setTransfers(prev => {
      const idx = prev.findIndex(t => t.id === incoming.id);
      if (idx >= 0) {
        const updated = [...prev];
        updated[idx] = { ...prev[idx], ...incoming };
        return updated;
      }
      return [incoming, ...prev];
    });
  }, [subResult.data, employee.id]);

  // Update canTransfer from subscription
  useEffect(() => {
    if (!statusSubResult.data?.onTransferStatusChanged) return;
    const { transferStatus } = statusSubResult.data.onTransferStatusChanged;
    if (transferStatus.employeeId !== employee.id) return;
    setCanTransfer(transferStatus.canTransfer);
    setTransferLimits(transferStatus);
  }, [statusSubResult.data, employee.id]);

  // Fetch bank accounts via REST
  useEffect(() => {
    setLoadingAccounts(true);
    fetch(`/api/bankaccounts/employee/${employee.id}`)
      .then(res => res.ok ? res.json() : Promise.reject('Failed to load bank accounts'))
      .then(data => {
        setBankAccounts(data);
        if (data.length > 0) {
          setForm(f => ({ ...f, bankAccountId: data[0].id }));
        }
      })
      .catch(() => setBankAccounts([]))
      .finally(() => setLoadingAccounts(false));
  }, [employee.id]);

  const handleSubmit = useCallback(async (e) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      const res = await fetch('/api/Transfer', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          employeeId: employee.id,
          amount: customAmount ? parseFloat(customAmount) : selectedAmount,
          payPeriodNumber: parseInt(payPeriod, 10),
          bankAccountId: form.bankAccountId,
        }),
      });

      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || `HTTP ${res.status}`);
      }

      const data = await res.json();
      // Insert placeholder entry into history — replaced by subscription when real events arrive
      setTransfers(prev => [{
        id: data.id,
        employeeId: employee.id,
        amount: customAmount ? parseFloat(customAmount) : selectedAmount,
        payPeriodNumber: parseInt(payPeriod, 10),
        status: 'Queued',
        initiatedAt: new Date().toISOString(),
        completedAt: null,
        failureReason: null,
        externalReferenceId: null,
        currentBalance: null,
        workflowSteps: null,
      }, ...prev]);
      setSelectedAmount(null);
      setCustomAmount('');
    } catch (err) {
      setError(err.message);
    } finally {
      setSubmitting(false);
    }
  }, [employee.id, form, payPeriod, selectedAmount, customAmount]);

  const handleAccept = useCallback(async (transferId, accepted) => {
    try {
      const res = await fetch(`/api/Transfer/${transferId}/accept`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ accepted }),
      });
      if (!res.ok) {
        const text = await res.text();
        setError(text || `HTTP ${res.status}`);
      } else {
        // Optimistically update status while waiting for subscription event
        setTransfers(prev => prev.map(t =>
          t.id === transferId
            ? { ...t, status: accepted ? 'AcceptPending' : 'RejectPending' }
            : t
        ));
      }
    } catch (err) {
      setError(err.message);
    }
  }, []);

  const formatCurrency = (val) => `$${Number(val).toFixed(2)}`;

  const statusClass = (status) => {
    switch (status) {
      case 'Completed': return 'transfer-status-success';
      case 'Failed': return 'transfer-status-danger';
      case 'AwaitingConfirmation': return 'transfer-status-info';
      case 'AcceptPending': return 'transfer-status-info';
      case 'RejectPending': return 'transfer-status-danger';
      default: return 'transfer-status-warning';
    }
  };

  const statusLabel = (status) => {
    switch (status) {
      case 'AcceptPending': return 'Accepting...';
      case 'RejectPending': return 'Rejecting...';
      default: return status;
    }
  };

  return (
    <div className="confirm-modal-overlay" onClick={onClose}>
      <div className="transfer-panel" onClick={(e) => e.stopPropagation()}>
        <div className="transfer-panel-header">
          <div className="transfer-panel-title">
            {onBack && (
              <button className="btn btn-secondary btn-back" onClick={onBack}>&larr;</button>
            )}
            <h3>Transfer - {employee.firstName} {employee.lastName}</h3>
          </div>
          <button className="btn btn-secondary" onClick={onClose}>Close</button>
        </div>

        <div className="transfer-balance-summary">
          <span className="transfer-balance-label">Available (Period {payPeriod})</span>
          <span className="transfer-balance-amount">{formatCurrency(availableBalance)}</span>
        </div>
        {!canTransfer && transferLimits && (
          <div className="transfer-limit-details" style={{ marginTop: '8px' }}>
            {transferLimits.periodCountLimitReached && (
              <div className="transfer-limit-item">
                Pay period transfer limit reached ({transferLimits.transferCount}/{transferLimits.periodTransferLimit} transfers)
              </div>
            )}
            {transferLimits.periodAmountLimitReached && (
              <div className="transfer-limit-item">
                Pay period amount limit reached ({formatCurrency(transferLimits.totalAmountTransferred)} of {formatCurrency(transferLimits.periodAmountLimit)})
              </div>
            )}
            {transferLimits.dailyLimitReached && (
              <div className="transfer-limit-item">
                Daily transfer limit reached ({transferLimits.dailyTransferCount}/{transferLimits.dailyTransferLimit} today)
              </div>
            )}
          </div>
        )}

        {error && <div className="transfer-error">{error}</div>}

        {/* New Transfer Form */}
        <form className="transfer-form" onSubmit={handleSubmit}>
          <div className="transfer-form-fields">
            <div className="transfer-amount-row">
              <div className="transfer-field transfer-field-amount">
                <label>Amount ($)</label>
                <div className="transfer-amount-options">
                  {presetAmounts.map(amount => (
                    <button
                      key={amount}
                      type="button"
                      className={`transfer-amount-btn${selectedAmount === amount && !customAmount ? ' selected' : ''}`}
                      onClick={() => { setSelectedAmount(amount); setCustomAmount(''); }}
                    >
                      ${amount}
                    </button>
                  ))}
                </div>
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  placeholder="Other amount"
                  className="transfer-custom-amount"
                  value={customAmount}
                  onChange={(e) => { setCustomAmount(e.target.value); setSelectedAmount(null); }}
                />
              </div>
              <div className="transfer-field transfer-field-bank">
                <label>Bank Account</label>
                {loadingAccounts ? (
                  <select disabled><option>Loading...</option></select>
                ) : bankAccounts.length === 0 ? (
                  <select disabled><option>No accounts</option></select>
                ) : (
                  <select
                    required
                    value={form.bankAccountId}
                    onChange={(e) => setForm(f => ({ ...f, bankAccountId: e.target.value }))}
                  >
                    {bankAccounts.map(ba => (
                      <option key={ba.id} value={ba.id}>
                        {ba.bankName} - ****{ba.accountNumberMasked}
                      </option>
                    ))}
                  </select>
                )}
              </div>
            </div>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={submitting || !canTransfer || bankAccounts.length === 0 || !payPeriod || (!selectedAmount && (!customAmount || parseFloat(customAmount) <= 0))}
            >
              {submitting ? 'Submitting...' : 'Transfer'}
            </button>
          </div>
        </form>

        {/* Transfer History */}
        <div className="transfer-history">
          <h4>History</h4>
          {transferResult.fetching && transfers.length === 0 && (
            <div className="loading">Loading transfers...</div>
          )}
          {transfers.length === 0 && !transferResult.fetching && (
            <div className="transfer-empty">No transfers yet</div>
          )}
          {transfers.length > 0 && (
            <div className="transfer-cards">
              {[...transfers]
                .sort((a, b) => new Date(b.initiatedAt) - new Date(a.initiatedAt))
                .map(t => (
                <div key={t.id} className={`transfer-card ${statusClass(t.status)}`}>
                  <div className="transfer-card-header">
                    <span className={`transfer-status ${statusClass(t.status)}`}>
                      {statusLabel(t.status)}
                    </span>
                    <span className="transfer-card-amount">{formatCurrency(t.amount)}</span>
                  </div>
                  {t.workflowSteps && t.workflowSteps.length > 0 && (
                    <div className="workflow-steps">
                      {t.workflowSteps.map((step, i) => (
                        <div key={step.name} className={`workflow-step workflow-step-${step.status.toLowerCase()}`}>
                          <div className="workflow-step-indicator">
                            {step.status === 'Completed' && <span className="workflow-icon completed">&#10003;</span>}
                            {step.status === 'InProgress' && <span className="workflow-icon in-progress" />}
                            {step.status === 'Failed' && <span className="workflow-icon failed">&#10005;</span>}
                            {step.status === 'Pending' && <span className="workflow-icon pending" />}
                            {i < t.workflowSteps.length - 1 && (
                              <div className={`workflow-connector ${
                                step.status === 'Completed' ? 'completed' : ''
                              }`} />
                            )}
                          </div>
                          <div className="workflow-step-content">
                            <span className="workflow-step-name">
                              {step.name === 'BankTransfer' ? 'Bank Transfer' :
                               step.name === 'BalanceCheck' ? 'Balance Check' :
                               step.name === 'AwaitingConfirmation' ? 'Confirmation' :
                               step.name === 'FraudCheck' ? 'Fraud Check' :
                               step.name}
                            </span>
                            {step.name === 'BankTransfer' && step.retryCount > 0 && (
                              <span className="workflow-step-retry">Attempt {step.retryCount + 1}/3</span>
                            )}
                            {step.detail && step.status === 'Failed' && (
                              <span className="workflow-step-detail">{step.detail}</span>
                            )}
                            {step.completedAt && (
                              <span className="workflow-step-time">{new Date(step.completedAt).toLocaleTimeString()}</span>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                  <div className="transfer-card-details">
                    <div className="transfer-card-row">
                      <span className="transfer-card-label">Initiated</span>
                      <span>{new Date(t.initiatedAt).toLocaleString()}</span>
                    </div>
                    <div className="transfer-card-row">
                      <span className="transfer-card-label">Pay Period</span>
                      <span>{t.payPeriodNumber}</span>
                    </div>
                    {t.completedAt && (
                      <div className="transfer-card-row">
                        <span className="transfer-card-label">Completed</span>
                        <span>{new Date(t.completedAt).toLocaleString()}</span>
                      </div>
                    )}
                    {t.externalReferenceId && (
                      <div className="transfer-card-row">
                        <span className="transfer-card-label">Reference</span>
                        <span>{t.externalReferenceId}</span>
                      </div>
                    )}
                    {t.failureReason && (
                      <div className="transfer-card-row transfer-card-failure">
                        <span className="transfer-card-label">Reason</span>
                        <span>{t.failureReason}</span>
                      </div>
                    )}
                  </div>
                  {t.status === 'AwaitingConfirmation' && (
                    <div className="transfer-card-actions">
                      {t.currentBalance != null && (
                        <span className="transfer-balance">Balance: {formatCurrency(t.currentBalance)}</span>
                      )}
                      <div className="transfer-card-buttons">
                        <button className="btn btn-sm btn-accept" onClick={() => handleAccept(t.id, true)}>Accept</button>
                        <button className="btn btn-sm btn-reject" onClick={() => handleAccept(t.id, false)}>Reject</button>
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
