import { format } from 'date-fns';

const CHANGE_TYPE_COLORS = {
  created: 'green',
  updated: 'blue',
  activated: 'green',
  deactivated: 'red',
};

export default function EmployeeChangeCard({ change }) {
  const { employee, changeType, timestamp } = change;
  const color = CHANGE_TYPE_COLORS[changeType] || 'gray';

  return (
    <div className={`change-card border-${color}`}>
      <div className="change-header">
        <span className={`badge badge-${color}`}>{changeType}</span>
        <span className="timestamp">
          {format(new Date(timestamp), 'HH:mm:ss')}
        </span>
      </div>
      <div className="change-body">
        <h3>{employee.firstName} {employee.lastName}</h3>
        <p className="email">{employee.email}</p>
        {employee.payRate && (
          <p className="pay-info">
            {employee.payType} - ${employee.payRate}
            {(employee.payType === 'Salary' || employee.payType === '2') &&
              ` (${employee.payPeriodHours ?? 40} hrs/pp)`}
          </p>
        )}
      </div>
    </div>
  );
}
