using HotChocolate;
using HotChocolate.Types;
using ListenerApi.Data.Services;

namespace ListenerApi.GraphQL.Subscriptions;

[ExtendObjectType<EmployeeSubscription>]
public class TransferSubscription
{
    [Subscribe]
    [Topic("TransferChanges")]
    public TransferChange OnTransferChanged([EventMessage] TransferChange change)
        => change;
}
