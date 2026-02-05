using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using ListenerApi.Data.Services;

namespace ListenerApi.GraphQL.Subscriptions;

public class EmployeeSubscription
{
    [Subscribe]
    [Topic("EmployeeChanges")]
    public EmployeeChange OnEmployeeChanged([EventMessage] EmployeeChange change)
        => change;
}
