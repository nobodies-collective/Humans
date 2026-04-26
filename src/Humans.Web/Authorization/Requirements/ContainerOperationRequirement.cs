using Microsoft.AspNetCore.Authorization;

namespace Humans.Web.Authorization.Requirements;

public sealed class ContainerOperationRequirement : IAuthorizationRequirement
{
    public static readonly ContainerOperationRequirement Manage = new(nameof(Manage));

    public string OperationName { get; }

    private ContainerOperationRequirement(string operationName)
    {
        OperationName = operationName;
    }
}
