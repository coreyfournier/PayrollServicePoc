namespace PayrollService.Application.DTOs;

public record TaxInformationDto(
    Guid Id,
    Guid EmployeeId,
    string FederalFilingStatus,
    int FederalAllowances,
    decimal AdditionalFederalWithholding,
    string State,
    string StateFilingStatus,
    int StateAllowances,
    decimal AdditionalStateWithholding,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateTaxInformationDto(
    Guid EmployeeId,
    string FederalFilingStatus,
    int FederalAllowances,
    decimal AdditionalFederalWithholding,
    string State,
    string StateFilingStatus,
    int StateAllowances,
    decimal AdditionalStateWithholding);

public record UpdateTaxInformationDto(
    string FederalFilingStatus,
    int FederalAllowances,
    decimal AdditionalFederalWithholding,
    string State,
    string StateFilingStatus,
    int StateAllowances,
    decimal AdditionalStateWithholding);
