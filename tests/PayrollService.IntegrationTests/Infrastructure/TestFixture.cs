namespace PayrollService.IntegrationTests.Infrastructure;

/// <summary>
/// Shared fixture that verifies the Docker Compose stack is running
/// and provides shared test employee data seeded by the seed script.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    public PayrollApiClient Api { get; } = new();
    public DatabaseHelper Db { get; } = new();

    // Populated during initialization from the running stack
    public List<EmployeeResponse> Employees { get; private set; } = new();
    public Dictionary<Guid, List<BankAccountResponse>> BankAccounts { get; private set; } = new();

    public async Task InitializeAsync()
    {
        // Verify the stack is reachable
        try
        {
            Employees = await Api.GetEmployeesAsync();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach payroll-api at {ServiceEndpoints.PayrollApi}. " +
                "Ensure the Docker Compose stack is running: docker-compose up -d && docker-compose up seed",
                ex);
        }

        if (Employees.Count == 0)
            throw new InvalidOperationException(
                "No employees found. Run the seed script first: docker-compose up seed");

        // Load bank accounts for all employees
        foreach (var emp in Employees)
        {
            var accounts = await Api.GetBankAccountsAsync(emp.Id);
            BankAccounts[emp.Id] = accounts;
        }

        // Clean any leftover transfer data from previous test runs
        await Db.CleanTransfersAsync();
        await Db.CleanMySqlTransfersAsync();
    }

    public async Task DisposeAsync()
    {
        Api.Dispose();
        Db.Dispose();
        await Task.CompletedTask;
    }

    // Helper: find employee by name
    public EmployeeResponse GetEmployee(string firstName) =>
        Employees.First(e => e.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase));

    public BankAccountResponse GetBankAccount(Guid employeeId) =>
        BankAccounts[employeeId].First(a => a.IsActive);
}

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<TestFixture> { }
