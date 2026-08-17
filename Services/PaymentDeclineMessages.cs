namespace MeDan.Api.Services;

/// <summary>
/// Turns a Paystack/telco decline into something a student can act on.
///
/// Gateway responses are machine strings written for engineers — a real one is
/// <c>LOW_BALANCE_OR_PAYEE_LIMIT_REACHED_OR_NOT_ALLOWED</c>. Putting that in
/// front of someone trying to pay their rent tells them nothing about what to
/// do next, so every known code is mapped and anything unrecognised falls back
/// to safe wording rather than being shown raw.
/// </summary>
public static class PaymentDeclineMessages
{
    private const string Generic =
        "The payment did not go through. No money has left your account — you "
        + "can try again.";

    /// <param name="gatewayResponse">Paystack's `gateway_response` / message.</param>
    public static string Explain(string? gatewayResponse)
    {
        if (string.IsNullOrWhiteSpace(gatewayResponse)) return Generic;

        var code = gatewayResponse.Trim().ToUpperInvariant();

        // Balance and wallet limits — the most common Ghanaian MoMo decline,
        // and usually the only one the student can fix themselves.
        if (code.Contains("LOW_BALANCE") ||
            code.Contains("INSUFFICIENT") ||
            code.Contains("NOT_ENOUGH"))
        {
            return "Your Mobile Money balance is too low for this amount, or it "
                + "is above your wallet limit. Top up, or ask your network to "
                + "raise your limit, then try again.";
        }

        if (code.Contains("LIMIT"))
        {
            return "This amount is above the limit on your Mobile Money wallet. "
                + "Your network can raise it once your ID is verified.";
        }

        if (code.Contains("NOT_ALLOWED") || code.Contains("NOT_PERMITTED"))
        {
            return "Your network would not allow this payment. Check that your "
                + "wallet accepts merchant payments, then try again.";
        }

        if (code.Contains("TIMEOUT") || code.Contains("TIMED_OUT"))
        {
            return "The request timed out before you approved it. Try again and "
                + "approve the prompt as soon as it arrives.";
        }

        if (code.Contains("CANCEL") || code.Contains("ABANDON"))
        {
            return "The payment was cancelled before it completed. You can try "
                + "again whenever you are ready.";
        }

        if (code.Contains("PIN"))
        {
            return "That PIN was not accepted by your network. Try again "
                + "carefully — repeated attempts can lock your wallet.";
        }

        if (code.Contains("WRONG_NUMBER") || code.Contains("INVALID_NUMBER") ||
            code.Contains("SUBSCRIBER"))
        {
            return "That Mobile Money number was not recognised. Check the "
                + "digits and the network you chose.";
        }

        if (code.Contains("EXPIRED"))
        {
            return "The payment request expired. Start it again and approve the "
                + "prompt when it arrives.";
        }

        // Card-side declines, for the hosted-checkout path.
        if (code.Contains("DECLINE"))
        {
            return "Your bank declined the payment. Contact them, or pay with "
                + "Mobile Money instead.";
        }

        return Generic;
    }
}
