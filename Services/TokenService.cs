using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MeDan.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace MeDan.Api.Services;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Signing key. Must be ≥32 bytes. Never commit this — use user-secrets or env.</summary>
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "medan-api";
    public string Audience { get; set; } = "medan-admin";
    public int LifetimeHours { get; set; } = 12;
}

/// <summary>Issues the API's own JWTs for staff accounts (the admin dashboard's login).</summary>
public class TokenService
{
    /// <summary>Claim holding the AppUser id — how <c>CurrentUser</c> resolves API tokens.</summary>
    public const string UserIdClaim = "medan_uid";

    private readonly JwtOptions _options;

    public TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string token, DateTime expiresAt) Issue(AppUser user)
    {
        var expires = DateTime.UtcNow.AddHours(_options.LifetimeHours);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(UserIdClaim, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
