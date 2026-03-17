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
      changeType
      timestamp
    }
  }
`;

export const TRANSFER_CHANGE_SUBSCRIPTION = `
  subscription OnTransferChanged {
    onTransferChanged {
      transfer {
        id
        employeeId
        amount
        payPeriodNumber
        status
        initiatedAt
        completedAt
        failureReason
        externalReferenceId
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
      changeType
      timestamp
    }
  }
`;

export const TRANSFER_STATUS_SUBSCRIPTION = `
  subscription OnTransferStatusChanged {
    onTransferStatusChanged {
      transferStatus {
        employeeId
        canTransfer
        periodCountLimitReached
        periodAmountLimitReached
        dailyLimitReached
        transferCount
        totalAmountTransferred
        dailyTransferCount
        periodTransferLimit
        periodAmountLimit
        dailyTransferLimit
        payPeriodNumber
      }
      timestamp
    }
  }
`;
