using System.Text.Json;
using System.Text.Json.Serialization;
using HotChocolate;

namespace ListenerApi.Data.Entities;

public class TransferRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public long PayPeriodNumber { get; set; }
    public string Status { get; set; } = "Queued"; // Queued, Initiated, AwaitingConfirmation, Processing, Completed, Failed
    public decimal? CurrentBalance { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? ExternalReferenceId { get; set; }
    public DateTime UpdatedAt { get; set; }

    [GraphQLIgnore]
    public string? WorkflowStepsJson { get; set; }

    public List<WorkflowStepDto>? WorkflowSteps =>
        string.IsNullOrEmpty(WorkflowStepsJson)
            ? null
            : JsonSerializer.Deserialize<List<WorkflowStepDto>>(WorkflowStepsJson, WorkflowStepDto.JsonOptions);

    // Navigation
    public EmployeeRecord Employee { get; set; } = null!;
}

public class WorkflowStepDto
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Detail { get; set; }
    public int RetryCount { get; set; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
