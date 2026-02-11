namespace PayrollService.Infrastructure.Orleans.State;

[GenerateSerializer]
public class IndexState
{
    [Id(0)]
    public HashSet<Guid> EntityIds { get; set; } = new();
}
