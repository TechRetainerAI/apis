using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

/// <summary>Start a Paystack transaction for a booking. The payer is taken from the token.</summary>
public record InitializePaymentRequest
{
    [Required] public Guid BookingId { get; init; }

    /// <summary>"momoMtn" | "momoTelecel" | "card".</summary>
    public PaymentChannel Channel { get; init; } = PaymentChannel.MomoMtn;

    /// <summary>MoMo number. Falls back to the user's saved phone.</summary>
    [MaxLength(30)] public string? Phone { get; init; }
}

/// <summary>A payment attempt as the app sees it (mirrors Dart <c>PaymentIntent</c>).</summary>
public record PaymentResponse
{
    public string Reference { get; init; } = default!;
    public Guid BookingId { get; init; }

    /// <summary>Amount in GH₵ (not pesewas).</summary>
    public int Amount { get; init; }

    public string Channel { get; init; } = default!;
    public string Status { get; init; } = default!;
    public string? CheckoutUrl { get; init; }
    public string? AuthorizationCode { get; init; }

    /// <summary>The booking's state after this payment was applied, e.g. "paymentHeld".</summary>
    public string BookingStatus { get; init; } = default!;

    public DateTime CreatedAt { get; init; }

    /// <summary>True when no Paystack key is configured and the transaction was simulated.</summary>
    public bool Simulated { get; init; }

    /// <summary>
    /// Paystack is holding this Mobile Money charge until the customer submits
    /// the code they were sent. The app must collect it and POST it to
    /// <c>/api/payments/{reference}/submit-otp</c>; polling alone will never
    /// resolve.
    /// </summary>
    public bool RequiresOtp { get; init; }

    /// <summary>Paystack's own instruction to show the customer, when it sends one.</summary>
    public string? DisplayText { get; init; }
}

/// <summary>The code the customer received for a Mobile Money charge.</summary>
public record SubmitOtpRequest
{
    public string Otp { get; init; } = default!;
}
