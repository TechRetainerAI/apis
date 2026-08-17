using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Dtos;
using MeDan.Api.Helpers;
using MeDan.Api.Models;
using MeDan.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly IPaystackClient _paystack;
    private readonly ILogger<CompaniesController> _log;

    public CompaniesController(
        AppDbContext db,
        CurrentUser current,
        IPaystackClient paystack,
        ILogger<CompaniesController> log)
    {
        _db = db;
        _current = current;
        _paystack = paystack;
        _log = log;
    }

    // ---------------------------------------------------------- settlement

    /// <summary>
    /// Banks and mobile-money networks a payout can be sent to. Comes straight
    /// from Paystack so the codes are always ones it will accept.
    /// </summary>
    [HttpGet("banks")]
    public async Task<ActionResult<IEnumerable<BankOption>>> Banks(CancellationToken ct)
    {
        var banks = await _paystack.ListBanksAsync(ct);
        return banks
            .Select(b => new BankOption { Name = b.Name, Code = b.Code, Type = b.Type })
            .ToList();
    }

    /// <summary>Where this company's escrow releases are paid. Owner only.</summary>
    [HttpGet("{id:guid}/settlement")]
    public async Task<ActionResult<SettlementResponse>> GetSettlement(
        Guid id, CancellationToken ct)
    {
        var (company, error) = await RequireOwnerAsync(id, ct);
        if (error is not null) return error;

        return ToSettlement(company!);
    }

    /// <summary>
    /// Sets the payout destination. The account is resolved with Paystack first —
    /// the name is taken from Paystack, never from the caller — then a transfer
    /// recipient is created so payouts reference a code, not raw account details.
    /// </summary>
    [HttpPost("{id:guid}/settlement")]
    public async Task<ActionResult<SettlementResponse>> SetSettlement(
        Guid id, SetSettlementRequest req, CancellationToken ct)
    {
        var (company, error) = await RequireOwnerAsync(id, ct);
        if (error is not null) return error;

        PaystackAccount resolved;
        try
        {
            resolved = await _paystack.ResolveAccountAsync(
                req.BankCode, req.AccountNumber.Trim(), ct);
        }
        catch (PaystackException ex)
        {
            // A mistyped number must fail loudly here — not silently swallow a payout later.
            return BadRequest($"Could not verify that account: {ex.Message}");
        }

        PaystackRecipient recipient;
        try
        {
            recipient = await _paystack.CreateRecipientAsync(
                resolved.AccountName,
                req.BankCode,
                req.AccountNumber.Trim(),
                string.IsNullOrWhiteSpace(req.Type) ? "mobile_money" : req.Type,
                ct);
        }
        catch (PaystackException ex)
        {
            return BadRequest($"Could not register that payout account: {ex.Message}");
        }

        company!.SettlementBankCode = req.BankCode;
        company.SettlementAccountNumber = req.AccountNumber.Trim();
        company.SettlementAccountName = resolved.AccountName;
        company.PaystackRecipientCode = recipient.RecipientCode;
        company.SettlementUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Settlement account set for company {Company} ({Bank}/****{Last4}).",
            company.Id, req.BankCode, Last4(company.SettlementAccountNumber));

        return ToSettlement(company);
    }

    /// <summary>Resolves the company and rejects anyone who isn't its owner.</summary>
    private async Task<(Company? company, ActionResult? error)> RequireOwnerAsync(
        Guid id, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return (null, Unauthorized("Register first."));

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return (null, NotFound("Company not found."));

        // Payout details move money — the owner alone may see or change them.
        if (company.OwnerUserId != me.Id && me.Role != UserRole.Admin)
            return (null, Forbid());

        return (company, null);
    }

    private static SettlementResponse ToSettlement(Company c) => new()
    {
        Configured = c.CanReceivePayouts,
        BankCode = c.SettlementBankCode,
        AccountNumberMasked = Last4(c.SettlementAccountNumber) is { } l4
            ? $"******{l4}"
            : null,
        AccountName = c.SettlementAccountName,
        UpdatedAt = c.SettlementUpdatedAt,
    };

    private static string? Last4(string? account) =>
        string.IsNullOrWhiteSpace(account) || account.Length < 4
            ? null
            : account[^4..];

    /// <summary>Companies the current user owns or works for.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<CompanyResponse>>> Mine(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var companies = await _db.Companies
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Where(c => c.OwnerUserId == me.Id || c.Members.Any(m => m.UserId == me.Id))
            .ToListAsync(ct);

        return companies.Select(ToResponse).ToList();
    }

    /// <summary>Create a company. The caller becomes its Owner (and is promoted to the Owner role).</summary>
    [HttpPost]
    public async Task<ActionResult<CompanyResponse>> Create(CreateCompanyRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var company = new Company
        {
            Name = req.Name,
            Phone = req.Phone,
            Email = req.Email,
            Address = req.Address,
            OwnerUserId = me.Id
        };
        company.Apply(CompanyTier.Starter);

        // Owner is also a member (so member-based queries include them).
        company.Members.Add(new CompanyMember
        {
            UserId = me.Id,
            Role = CompanyRole.Owner,
            CanPostListings = true
        });

        if (me.Role == UserRole.Student) me.Role = UserRole.Owner;

        _db.Companies.Add(company);
        await _db.SaveChangesAsync(ct);

        var saved = await LoadAsync(company.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = company.Id }, ToResponse(saved!));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyResponse>> Get(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c is null) return NotFound();
        if (!await IsMember(c, ct)) return Forbid();
        return ToResponse(c);
    }

    /// <summary>Add a worker (by email) to the company. Owner only.</summary>
    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<CompanyResponse>> AddMember(Guid id, AddMemberRequest req, CancellationToken ct)
    {
        var company = await LoadAsync(id, ct);
        if (company is null) return NotFound();

        var me = await _current.GetAsync(ct: ct);
        if (me is null || company.OwnerUserId != me.Id) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (user is null) return NotFound($"No registered user with email {req.Email}.");

        if (company.Members.Any(m => m.UserId == user.Id))
            return Conflict("User is already a member.");

        // Added via the context, not company.Members: the company is tracked, and a new
        // member whose Id is already set would be treated as Modified (→ UPDATE, 0 rows).
        _db.Add(new CompanyMember
        {
            CompanyId = company.Id,
            UserId = user.Id,
            Role = req.Role,
            CanPostListings = req.CanPostListings
        });

        // A worker on a company is at least an Owner/Worker-type user, not a plain student.
        if (user.Role == UserRole.Student) user.Role = UserRole.Worker;

        await _db.SaveChangesAsync(ct);
        var saved = await LoadAsync(id, ct);
        return ToResponse(saved!);
    }

    /// <summary>Remove a worker. Owner only; the owner cannot remove themselves here.</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        var company = await LoadAsync(id, ct);
        if (company is null) return NotFound();

        var me = await _current.GetAsync(ct: ct);
        if (me is null || company.OwnerUserId != me.Id) return Forbid();
        if (userId == company.OwnerUserId) return BadRequest("The owner cannot be removed.");

        var member = company.Members.FirstOrDefault(m => m.UserId == userId);
        if (member is null) return NotFound("Not a member.");

        _db.CompanyMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<Company?> LoadAsync(Guid id, CancellationToken ct) =>
        _db.Companies.Include(c => c.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private async Task<bool> IsMember(Company c, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        return me is not null && (c.OwnerUserId == me.Id || c.Members.Any(m => m.UserId == me.Id));
    }

    private static CompanyResponse ToResponse(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Tier = c.Tier.ToCamel(),
        CommissionRate = c.CommissionRate,
        ListingLimit = c.ListingLimit,
        IsVerified = c.IsVerified,
        OwnerUserId = c.OwnerUserId,
        Members = c.Members.Select(m => new MemberResponse
        {
            UserId = m.UserId,
            FullName = m.User.FullName,
            Email = m.User.Email,
            Role = m.Role.ToCamel(),
            CanPostListings = m.CanPostListings
        }).ToList()
    };
}
