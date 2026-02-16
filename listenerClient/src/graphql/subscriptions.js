export const EMPLOYEE_CHANGE_SUBSCRIPTION = `
  subscription OnEmployeeChanged {
    onEmployeeChanged {
      employee {
        id
        firstName
        lastName
        email
        payType
        payRate
        payPeriodHours
        isActive
        lastEventType
        lastEventTimestamp
        updatedAt
      }
      changeType
      timestamp
    }
  }
`;
