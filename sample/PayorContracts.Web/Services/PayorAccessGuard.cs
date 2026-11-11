using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace PayorContracts.Web.Services;

/// <summary>
/// Defense-in-depth authorization for payor mutations, used by the page (UI gating) and by
/// <see cref="PayorService"/> (blocks the call regardless of the UI).
/// The allowed roles come from configuration <c>Authorization:PayorModifyRoles</c>. An empty list
/// keeps the Reference behavior (PayorList.aspx and PayorService.asmx have no access checks), so
/// every user may modify; once roles are configured, only authenticated users in one of them may.
/// </summary>
public sealed class PayorAccessGuard
{
    public const string ModifyRolesKey = "Authorization:PayorModifyRoles";

    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly ILogger<PayorAccessGuard> _logger;
    private readonly string[] _modifyRoles;

    public PayorAccessGuard(
        AuthenticationStateProvider authenticationStateProvider,
        IConfiguration configuration,
        ILogger<PayorAccessGuard> logger)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _logger = logger;
        _modifyRoles = configuration.GetSection(ModifyRolesKey).Get<string[]>()?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToArray() ?? Array.Empty<string>();
    }

    /// <summary>True when the current user may modify payors (page-level check).</summary>
    public async Task<bool> IsAuthorizedToModifyAsync()
    {
        var user = await GetUserAsync();
        return IsAuthorized(user);
    }

    /// <summary>
    /// Service-level guard: throws <see cref="UnauthorizedAccessException"/> and logs the denial
    /// when the current user may not modify payors.
    /// </summary>
    public async Task EnforceModifyAccessAsync(string operation)
    {
        var user = await GetUserAsync();
        if (IsAuthorized(user))
            return;

        _logger.LogWarning("Denied payor {Operation} for user {User}", operation, user.Identity?.Name ?? "(anonymous)");
        throw new UnauthorizedAccessException($"You are not allowed to {operation}.");
    }

    private bool IsAuthorized(ClaimsPrincipal user)
    {
        if (_modifyRoles.Length == 0)
            return true;

        return user.Identity?.IsAuthenticated == true && _modifyRoles.Any(user.IsInRole);
    }

    private async Task<ClaimsPrincipal> GetUserAsync()
    {
        try
        {
            var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
            return state.User;
        }
        catch (InvalidOperationException ex)
        {
            // No authentication state yet (outside a request/circuit): treat as anonymous
            _logger.LogDebug(ex, "Authentication state unavailable; treating the user as anonymous");
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
    }
}
