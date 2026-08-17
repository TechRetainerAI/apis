using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>A payment attempt against a booking (Paystack: MoMo / card).</summary>
public class Payment
{
    /// <summary>Payment provider reference. Primary key.</summary>
    [MaxLength(100)]
    public string Reference { get; set; } = default!;

    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = default!;

    public int Amount { get; set; }
    public PaymentChannel Channel { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Initialized;

    [MaxLength(500)]
    public string? CheckoutUrl { get; set; }

    [MaxLength(100)]
    public string? AuthorizationCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
