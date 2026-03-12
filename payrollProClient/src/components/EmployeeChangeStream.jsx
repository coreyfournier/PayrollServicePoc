import { useSubscription } from 'urql';
import { EMPLOYEE_CHANGE_SUBSCRIPTION } from '../graphql/subscriptions';
import { useState, useEffect } from 'react';
import EmployeeChangeCard from './EmployeeChangeCard';

export default function EmployeeChangeStream() {
  const [changes, setChanges] = useState([]);
  const [result] = useSubscription({ query: EMPLOYEE_CHANGE_SUBSCRIPTION });

  useEffect(() => {
    if (result.data?.onEmployeeChanged) {
      const newChange = result.data.onEmployeeChanged;
      setChanges(prev => [newChange, ...prev].slice(0, 50));
    }
  }, [result.data]);

  if (result.error) {
    return <div className="error">Error: {result.error.message}</div>;
  }

  return (
    <div className="change-stream">
      <h2>Employee Change Stream</h2>
      <div className="connection-status">
        {result.fetching ? '🟡 Connecting...' : '🟢 Connected'}
      </div>
      {changes.length === 0 && (
        <div className="empty-state">
          Listening for employee changes...
        </div>
      )}
      <div className="change-list">
        {changes.map((change, idx) => (
          <EmployeeChangeCard key={idx} change={change} />
        ))}
      </div>
    </div>
  );
}
