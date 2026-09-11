using Microsoft.AspNetCore.SignalR;
using PIYA_API.Service.Interface;

namespace PIYA_API.Hubs;

/// <summary>Re-check permissions for calls made over an already-open socket.</summary>
public sealed class CurrentSessionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var jwt = context.ServiceProvider.GetRequiredService<IJwtService>();
        if (context.Context.User is null || !await jwt.IsSessionCurrentAsync(context.Context.User))
        {
            context.Context.Abort();
            throw new HubException("This session has ended. Sign in again.");
        }
        return await next(context);
    }
}
