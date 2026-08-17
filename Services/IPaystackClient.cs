using MeDan.Api.Models;

namespace MeDan.Api.Services;

/// <summary>Result of initializing a Paystack transaction.</summary>
public record PaystackInitResult(string Reference, string? CheckoutUrl, string? AccessCode);

/// <summary>Where a direct mobile-money charge got to.</summary>
public enum PaystackChargeState
{
    /// <summary>Prompt is on the handset; poll verify until it resolves.</summary>
    PendingApproval = 0,

    /// <summary>Paystack wants an OTP from the customer before it can proceed.</summary>
    NeedsOtp = 1,

    /// <summary>Settled immediately (a saved authorisation, usually).</summary>
    Succeeded = 2,

    /// <summary>Rejected outright — see <see cref="PaystackChargeResult.Message"/>.</summary>
    Failed = 3
}

public record PaystackChargeResult(
    string Reference,
    PaystackChargeState State,
    string? Message,
    string? DisplayText);

/// <summary>A payout destination Paystack will accept — a bank or a MoMo network.</summary>
public record PaystackBank(string Name, string Code, string Type, string Currency);

/// <summary>The account holder Paystack resolved for a bank code + account number.</summary>
public record PaystackAccount(string AccountNumber, string AccountName);

/// <summary>A stored transfer destination ("RCP_...").</summary>
public record PaystackRecipient(string RecipientCode, string AccountName);

/// <summary>Outcome of an outbound transfer.</summary>
public record PaystackTransferResult(
    string TransferCode,
    PayoutStatus Status,
    string? Message);

/// <summary>Outcome of a refund request. Paystack settles refunds asynchronously.</summary>
public record PaystackRefundResult(string RefundId, PayoutStatus Status, string? Message);

/// <summary>What Paystack reports for a reference once the customer has (or hasn't) paid.</summary>
public record PaystackVerifyResult(
    string Reference,
    PaymentStatus Status,
    int AmountPesewas,
    PaymentChannel Channel,
    string? GatewayResponse);

/// <summary>
/// Thin wrapper over the Paystack REST API. The secret key never leaves the server —
/// the app only ever sees references and checkout URLs.
/// </summary>
public interface IPaystackClient
{
    /// <summary>True when no secret key is configured and payments are being simulated (dev only).</summary>
    bool IsSimulated { get; }

    /// <summary>Create a transaction and get back a reference + checkout URL.</summary>
    Task<PaystackInitResult> InitializeAsync(
        string email,
        int amountGhs,
        string reference,
        PaymentChannel channel,
        string? phone,
        IDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Charges a Ghanaian Mobile Money wallet directly, pushing the approval
    /// prompt to the handset instead of routing through a checkout page.
    ///
    /// <c>transaction/initialize</c> only returns a hosted page; a student who
    /// never finishes that page leaves the transaction "abandoned", which is
    /// the single biggest source of dropped MoMo payments. This is the flow
    /// Paystack intends for Ghana MoMo.
    /// </summary>
    Task<PaystackChargeResult> ChargeMobileMoneyAsync(
        string email,
        int amountGhs,
        string reference,
        PaymentChannel channel,
        string phone,
        IDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Completes a charge that came back <see cref="PaystackChargeState.NeedsOtp"/>
    /// by submitting the code the customer received.
    /// </summary>
    Task<PaystackChargeResult> SubmitOtpAsync(
        string reference, string otp, CancellationToken ct = default);

    /// <summary>Ask Paystack for the authoritative state of a reference.</summary>
    Task<PaystackVerifyResult> VerifyAsync(string reference, CancellationToken ct = default);

    /// <summary>Banks and mobile-money networks that can receive a payout.</summary>
    Task<IReadOnlyList<PaystackBank>> ListBanksAsync(CancellationToken ct = default);

    /// <summary>
    /// Confirms an account exists and returns the name on it. Used so an owner
    /// can't mistype a number and have payouts vanish — the name is never taken
    /// from user input.
    /// </summary>
    Task<PaystackAccount> ResolveAccountAsync(
        string bankCode, string accountNumber, CancellationToken ct = default);

    /// <summary>Creates (or re-creates) the transfer recipient a payout is sent to.</summary>
    Task<PaystackRecipient> CreateRecipientAsync(
        string name,
        string bankCode,
        string accountNumber,
        string type,
        CancellationToken ct = default);

    /// <summary>
    /// Sends money to a stored recipient. <paramref name="reference"/> is our
    /// idempotency key — Paystack rejects a repeat of the same reference, so a
    /// retry cannot pay twice.
    /// </summary>
    Task<PaystackTransferResult> TransferAsync(
        string recipientCode,
        int amountGhs,
        string reference,
        string? reason,
        CancellationToken ct = default);

    /// <summary>Refunds a settled charge back to the payer. Settles asynchronously.</summary>
    Task<PaystackRefundResult> RefundAsync(
        string transactionReference,
        int amountGhs,
        CancellationToken ct = default);

    /// <summary>HMAC-SHA512 check of a webhook body against the <c>x-paystack-signature</c> header.</summary>
    bool IsValidWebhookSignature(string rawBody, string? signatureHeader);
}
