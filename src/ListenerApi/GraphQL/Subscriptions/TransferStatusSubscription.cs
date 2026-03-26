using HotChocolate;
using HotChocolate.Types;
using ListenerApi.Data.Services;

namespace ListenerApi.GraphQL.Subscriptions;

[ExtendObjectType<EmployeeSubscription>]
public class TransferStatusSubscription
{
    [Subscribe]
    [Topic("TransferStatusChanges")]
    public TransferStatusChange OnTransferStatusChanged([EventMessage] TransferStatusChange change)
        => change;
}
