using System.Security.Claims;
using MeDan.Api.Data;
using MeDan.Api.Models;
using MeDan.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Auth;

/// <summary>
/// Resolves the AppUser row for the caller, based on the Firebase UID in the validated JWT.
/// Scoped per-request.
/// </summary>
public class CurrentUser
{
    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext _db;

    public CurrentUser(IHttpContextAccessor http, AppDbContext db)
    {
        _http = http;
        _db = db;
    }

    /// <summary>AppUser id carried by the MeDan token. Null when unauthenticated.</summary>
    public Guid? ApiUserId =>
        Guid.TryParse(_http.HttpContext?.User.FindFirstValue(TokenService.UserIdClaim), out var id)
            ? id
            : null;

    public string? Email =>
        _http.HttpContext?.User.FindFirstValue("email")
        ?? _http.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    /// <summary>The AppUser row for the caller, or null when unauthenticated.</summary>
    public Task<AppUser?> GetAsync(bool includeStudent = false, CancellationToken ct = default)
    {
        if (ApiUserId is not Guid id) return Task.FromResult<AppUser?>(null);

        IQueryable<AppUser> q = _db.Users;
        if (includeStudent) q = q.Include(u => u.StudentProfile);
        return q.FirstOrDefaultAsync(u => u.Id == id, ct);
    }
}
