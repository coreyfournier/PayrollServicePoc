import { User } from 'lucide-react';

const EMPLOYEES = ['John', 'Sarah', 'Michael', 'Emily', 'David'];

export default function ChatIdentityBar({ employeeName, onNameChange }) {
  return (
    <div className="chat-identity-bar">
      <User size={16} />
      <span className="chat-identity-label">Chatting as:</span>
      <select
        className="chat-identity-select"
        value={employeeName}
        onChange={(e) => onNameChange(e.target.value)}
      >
        <option value="">Select employee...</option>
        {EMPLOYEES.map((name) => (
          <option key={name} value={name}>{name}</option>
        ))}
      </select>
    </div>
  );
}
