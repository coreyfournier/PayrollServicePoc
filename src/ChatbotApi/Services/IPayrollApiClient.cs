namespace ChatbotApi.Services;

public interface IPayrollApiClient
{
    Task<string> GetAllEmployeesAsync();
    Task<string> GetEmployeeByIdAsync(string employeeId);
    Task<string> GetTimeEntriesAsync(string employeeId);
    Task<string> GetTaxInformationAsync(string employeeId);
    Task<string> GetDeductionsAsync(string employeeId);
}
