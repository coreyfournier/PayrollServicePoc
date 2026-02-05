export const GET_ALL_EMPLOYEES = `
  query GetEmployees {
    employees {
      id
      firstName
      lastName
      email
      payType
      payRate
      isActive
      lastEventType
      lastEventTimestamp
      createdAt
      updatedAt
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
