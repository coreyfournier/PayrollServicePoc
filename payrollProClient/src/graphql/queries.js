export const GET_ALL_EMPLOYEES = `
  query GetEmployees {
    employees {
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
      createdAt
      updatedAt
      payAttributes {
        grossPay
        federalTax
        stateTax
        additionalFederalWithholding
        additionalStateWithholding
        totalTax
        totalFixedDeductions
        totalPercentDeductions
        totalDeductions
        netPay
        payRate
        payType
        totalHoursWorked
        payPeriodStart
        payPeriodEnd
        payPeriodNumber
        transferCount
        transferTotalAmount
      }
    }
  }
`;

export const GET_TRANSFERS_BY_EMPLOYEE = `
  query GetTransfersByEmployee($employeeId: UUID!) {
    transfersByEmployeeId(employeeId: $employeeId) {
      id
      employeeId
      amount
      payPeriodNumber
      status
      initiatedAt
      completedAt
      failureReason
      externalReferenceId
      currentBalance
      updatedAt
      workflowSteps {
        name
        status
        startedAt
        completedAt
        detail
        retryCount
      }
    }
  }
`;

export const GET_ALL_TRANSFERS = `
  query GetAllTransfers {
    transfers {
      id
      employeeId
      status
      amount
      currentBalance
    }
  }
`;

export const DELETE_ALL_EMPLOYEES = `
  mutation DeleteAllEmployees {
    deleteAllEmployees {
      deletedCount
      success
      message
    }
  }
`;
