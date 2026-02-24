using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace PayrollService.Api.Auth;

public class KeycloakClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity == null || !identity.IsAuthenticated)
            return Task.FromResult(principal);

        // Extract roles from Keycloak's realm_access.roles claim
        var realmAccessClaim = identity.FindFirst("realm_access");
        if (realmAccessClaim != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(realmAccessClaim.Value);
                if (doc.RootElement.TryGetProperty("roles", out var roles))
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        var roleValue = role.GetString();
                        if (roleValue != null && !identity.HasClaim(ClaimTypes.Role, roleValue))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Role, roleValue));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed claim — skip silently
            }
        }

        return Task.FromResult(principal);
    }
}
