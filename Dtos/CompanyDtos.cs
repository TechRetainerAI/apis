using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

public record CreateCompanyRequest
{
    [Required, MaxLength(150)] public string Name { get; init; } = default!;
    [MaxLength(30)] public string? Phone { get; init; }
    [MaxLength(256)] public string? Email { get; init; }
    [MaxLength(300)] public string? Address { get; init; }
}

public record AddMemberRequest
{
    /// <summary>Email of an existing registered user to add as a worker.</summary>
    [Required, EmailAddress] public string Email { get; init; } = default!;
    public CompanyRole Role { get; init; } = CompanyRole.Worker;
    public bool CanPostListings { get; init; } = true;
}

public record MemberResponse
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string Role { get; init; } = default!;
    public bool CanPostListings { get; init; }
}

public record CompanyResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Tier { get; init; } = default!;
    public decimal CommissionRate { get; init; }
    public int? ListingLimit { get; init; }
    public bool IsVerified { get; init; }
    public Guid OwnerUserId { get; init; }
    public List<MemberResponse> Members { get; init; } = new();
}

// ---------- Settlement ----------

/// <summary>A payout destination offered to the owner (bank or MoMo network).</summary>
public record BankOption
{
    public string Name { get; init; } = default!;
    public string Code { get; init; } = default!;
    /// <summary>"mobile_money" or a bank type such as "ghipss".</summary>
    public string Type { get; init; } = default!;
}

/// <summary>Body for POST /api/companies/{id}/settlement.</summary>
public record SetSettlementRequest
{
    [Required, MaxLength(20)] public string BankCode { get; init; } = default!;
    [Required, MaxLength(30)] public string AccountNumber { get; init; } = default!;

    /// <summary>"mobile_money" for MoMo, otherwise the bank type from /banks.</summary>
    [MaxLength(30)] public string Type { get; init; } = "mobile_money";
}

/// <summary>Where a company's escrow releases are paid out to.</summary>
public record SettlementResponse
{
    public bool Configured { get; init; }
    public string? BankCode { get; init; }

    /// <summary>Masked — only the last 4 digits are ever returned.</summary>
    public string? AccountNumberMasked { get; init; }

    /// <summary>Name Paystack resolved for the account, not user-supplied.</summary>
    public string? AccountName { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
