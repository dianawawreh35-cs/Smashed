using CallCenter.Server.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CallCenter.Server.Hubs;

/// <summary>
/// Realtime channel between the server and the WPF Agent App: incoming-call
/// screen pops, call state changes, agent presence and task assignment.
/// </summary>
/// <remarks>
/// Stub for now - methods and the strongly typed client interface are added
/// with the PBX/telephony features. Mapped at <c>/hubs/agent</c>.
/// </remarks>
[Authorize(AuthPolicies.SignedIn)]
public class AgentHub(ILogger<AgentHub> logger) : Hub
{
    private readonly ILogger<AgentHub> _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Agent hub connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Agent hub disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
