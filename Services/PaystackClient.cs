using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeDan.Api.Models;
using Microsoft.Extensions.Options;

namespace MeDan.Api.Services;

/// <summary>
/// Paystack REST client. Amounts are sent in <b>pesewas</b> (GH₵ × 100).
///
/// If no secret key is configured and the host is in Development, the client runs in
/// <b>simulation mode</b>: initialize returns a fake reference and verify reports success,
/// so the booking → payment → escrow flow can be exercised without a Paystack account.
/// Simulation is never enabled outside Development.
/// </summary>
public class PaystackClient : IPaystackClient
{
    private readonly HttpClient _http;
    private readonly PaystackOptions _opt;
    private readonly bool _simulate;
    private readonly ILogger<PaystackClient> _log;

    public PaystackClient(
        HttpClient http,
        IOptions<PaystackOptions> opt,
        IHostEnvironment env,
        ILogger<PaystackClient> log)
    {
        _http = http;
        _opt = opt.Value;
        _log = log;

        var hasKey = !string.IsNullOrWhiteSpace(_opt.SecretKey);
        _simulate = !hasKey && env.IsDevelopment();

        if (!hasKey && !env.IsDevelopment())
            throw new InvalidOperationException(
                "Paystack:SecretKey is not configured. Set it via user-secrets or the " +
                "Paystack__SecretKey environment variable before running outside Development.");

        if (_simulate)
            _log.LogWarning(
                "Paystack:SecretKey is not set — running in SIMULATION mode. Payments are faked; " +
                "never deploy this configuration.");

        _http.BaseAddress = new Uri(_opt.BaseUrl.TrimEnd('/') + "/");
        if (hasKey)
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _opt.SecretKey);
    }

    public bool IsSimulated => _simulate;

    public async Task<PaystackInitResult> InitializeAsync(
        string email,
        int amountGhs,
        string reference,
        PaymentChannel channel,
        string? phone,
        IDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackInitResult(reference, null, null);

        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["amount"] = amountGhs * 100,          // pesewas
            ["currency"] = _opt.Currency,
            ["reference"] = reference,
            ["channels"] = ChannelsFor(channel),
            ["callback_url"] = _opt.CallbackUrl,
            ["metadata"] = BuildMetadata(channel, phone, metadata)
        };

        using var res = await _http.PostAsJsonAsync("transaction/initialize", body, ct);
        var json = await ReadEnvelopeAsync(res, "initialize", ct);

        var data = json.GetProperty("data");
        return new PaystackInitResult(
            data.TryGetProperty("reference", out var r) ? r.GetString() ?? reference : reference,
            data.TryGetProperty("authorization_url", out var u) ? u.GetString() : null,
            data.TryGetProperty("access_code", out var a) ? a.GetString() : null);
    }

    public async Task<PaystackChargeResult> ChargeMobileMoneyAsync(
        string email,
        int amountGhs,
        string reference,
        PaymentChannel channel,
        string phone,
        IDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackChargeResult(
                reference, PaystackChargeState.Succeeded, "simulated", null);

        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["amount"] = amountGhs * 100,          // pesewas
            ["currency"] = _opt.Currency,
            ["reference"] = reference,
            ["mobile_money"] = new Dictionary<string, string>
            {
                ["phone"] = NormalisePhone(phone),
                ["provider"] = ProviderFor(channel)
            },
            ["metadata"] = BuildMetadata(channel, phone, metadata)
        };

        using var res = await _http.PostAsJsonAsync("charge", body, ct);
        var json = await ReadEnvelopeAsync(res, "charge", ct);

        var data = json.GetProperty("data");
        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        var display = data.TryGetProperty("display_text", out var d) ? d.GetString() : null;
        var message = json.TryGetProperty("message", out var m) ? m.GetString() : null;

        // Paystack's charge states, in its own vocabulary.
        var state = status switch
        {
            "success" => PaystackChargeState.Succeeded,
            "send_otp" => PaystackChargeState.NeedsOtp,
            // pay_offline / pending / send_pin all mean "the customer is being
            // asked something on their handset" — poll verify from here.
            "pay_offline" or "pending" or "send_pin" or "open_url"
                => PaystackChargeState.PendingApproval,
            _ => PaystackChargeState.Failed
        };

        _log.LogInformation(
            "MoMo charge {Reference}: Paystack status '{Status}' → {State}.",
            reference, status, state);

        return new PaystackChargeResult(reference, state, display ?? message, display);
    }

    public async Task<PaystackChargeResult> SubmitOtpAsync(
        string reference, string otp, CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackChargeResult(
                reference, PaystackChargeState.Succeeded, "simulated", null);

        var body = new Dictionary<string, object?>
        {
            ["otp"] = otp.Trim(),
            ["reference"] = reference
        };

        using var res = await _http.PostAsJsonAsync("charge/submit_otp", body, ct);
        var json = await ReadEnvelopeAsync(res, "submit OTP", ct);

        var data = json.GetProperty("data");
        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        var display = data.TryGetProperty("display_text", out var d) ? d.GetString() : null;
        var message = json.TryGetProperty("message", out var m) ? m.GetString() : null;

        var state = status switch
        {
            "success" => PaystackChargeState.Succeeded,
            // A wrong code comes back asking for the OTP again.
            "send_otp" => PaystackChargeState.NeedsOtp,
            "pay_offline" or "pending" or "send_pin"
                => PaystackChargeState.PendingApproval,
            _ => PaystackChargeState.Failed
        };

        _log.LogInformation(
            "OTP submitted for {Reference}: Paystack status '{Status}' → {State}.",
            reference, status, state);

        return new PaystackChargeResult(reference, state, display ?? message, display);
    }

    /// <summary>Paystack expects the local Ghanaian form, e.g. 0241234567.</summary>
    private static string NormalisePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("233") && digits.Length >= 12) return "0" + digits[3..];
        if (!digits.StartsWith('0') && digits.Length == 9) return "0" + digits;
        return digits;
    }

    /// <summary>Paystack's Ghanaian mobile-money provider codes.</summary>
    private static string ProviderFor(PaymentChannel channel) => channel switch
    {
        PaymentChannel.MomoTelecel => "vod",   // Telecel, formerly Vodafone Cash
        _ => "mtn"
    };

    public async Task<PaystackVerifyResult> VerifyAsync(string reference, CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackVerifyResult(reference, PaymentStatus.Success, 0, PaymentChannel.MomoMtn, "simulated");

        using var res = await _http.GetAsync($"transaction/verify/{Uri.EscapeDataString(reference)}", ct);
        var json = await ReadEnvelopeAsync(res, "verify", ct);

        var data = json.GetProperty("data");
        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        var amount = data.TryGetProperty("amount", out var a) && a.TryGetInt32(out var amt) ? amt : 0;
        var channel = data.TryGetProperty("channel", out var c) ? c.GetString() : null;
        var gateway = data.TryGetProperty("gateway_response", out var g) ? g.GetString() : null;

        return new PaystackVerifyResult(reference, MapStatus(status), amount, MapChannel(channel), gateway);
    }

    public async Task<IReadOnlyList<PaystackBank>> ListBanksAsync(CancellationToken ct = default)
    {
        if (_simulate)
            return new[]
            {
                new PaystackBank("MTN Mobile Money", "MTN", "mobile_money", "GHS"),
                new PaystackBank("Telecel Cash", "VOD", "mobile_money", "GHS"),
                new PaystackBank("AirtelTigo Money", "ATL", "mobile_money", "GHS"),
                new PaystackBank("Simulated Bank", "000", "ghipss", "GHS"),
            };

        using var res = await _http.GetAsync(
            $"bank?currency={_opt.Currency}&perPage=200", ct);
        var json = await ReadEnvelopeAsync(res, "list banks", ct);

        var banks = new List<PaystackBank>();
        foreach (var b in json.GetProperty("data").EnumerateArray())
        {
            var code = b.TryGetProperty("code", out var c) ? c.GetString() : null;
            var name = b.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (code is null || name is null) continue;

            banks.Add(new PaystackBank(
                name,
                code,
                b.TryGetProperty("type", out var t) ? t.GetString() ?? "ghipss" : "ghipss",
                b.TryGetProperty("currency", out var cu) ? cu.GetString() ?? _opt.Currency : _opt.Currency));
        }
        return banks;
    }

    public async Task<PaystackAccount> ResolveAccountAsync(
        string bankCode, string accountNumber, CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackAccount(accountNumber, "SIMULATED ACCOUNT HOLDER");

        using var res = await _http.GetAsync(
            $"bank/resolve?account_number={Uri.EscapeDataString(accountNumber)}" +
            $"&bank_code={Uri.EscapeDataString(bankCode)}", ct);
        var json = await ReadEnvelopeAsync(res, "resolve account", ct);

        var data = json.GetProperty("data");
        return new PaystackAccount(
            data.TryGetProperty("account_number", out var a) ? a.GetString() ?? accountNumber : accountNumber,
            data.TryGetProperty("account_name", out var n) ? n.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<PaystackRecipient> CreateRecipientAsync(
        string name,
        string bankCode,
        string accountNumber,
        string type,
        CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackRecipient($"RCP_SIM_{accountNumber}", name);

        var body = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["name"] = name,
            ["account_number"] = accountNumber,
            ["bank_code"] = bankCode,
            ["currency"] = _opt.Currency,
        };

        using var res = await _http.PostAsJsonAsync("transferrecipient", body, ct);
        var json = await ReadEnvelopeAsync(res, "create recipient", ct);

        var data = json.GetProperty("data");
        var code = data.TryGetProperty("recipient_code", out var rc) ? rc.GetString() : null;
        if (code is null)
            throw new PaystackException("Paystack did not return a recipient code.");

        var details = data.TryGetProperty("details", out var d) ? d : default;
        var accountName = details.ValueKind == JsonValueKind.Object &&
                          details.TryGetProperty("account_name", out var an)
            ? an.GetString() ?? name
            : name;

        return new PaystackRecipient(code, accountName);
    }

    public async Task<PaystackTransferResult> TransferAsync(
        string recipientCode,
        int amountGhs,
        string reference,
        string? reason,
        CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackTransferResult($"TRF_SIM_{reference}", PayoutStatus.Paid, "simulated");

        var body = new Dictionary<string, object?>
        {
            ["source"] = "balance",
            ["amount"] = amountGhs * 100,      // pesewas
            ["recipient"] = recipientCode,
            ["reference"] = reference,          // idempotency key
            ["reason"] = reason,
            ["currency"] = _opt.Currency,
        };

        using var res = await _http.PostAsJsonAsync("transfer", body, ct);
        var json = await ReadEnvelopeAsync(res, "transfer", ct);

        var data = json.GetProperty("data");
        var code = data.TryGetProperty("transfer_code", out var tc) ? tc.GetString() : null;
        var status = data.TryGetProperty("status", out var st) ? st.GetString() : null;

        return new PaystackTransferResult(
            code ?? reference,
            MapTransferStatus(status),
            data.TryGetProperty("message", out var m) ? m.GetString() : null);
    }

    public async Task<PaystackRefundResult> RefundAsync(
        string transactionReference,
        int amountGhs,
        CancellationToken ct = default)
    {
        if (_simulate)
            return new PaystackRefundResult(
                $"RFD_SIM_{transactionReference}", PayoutStatus.Refunded, "simulated");

        var body = new Dictionary<string, object?>
        {
            ["transaction"] = transactionReference,
            ["amount"] = amountGhs * 100,
        };

        using var res = await _http.PostAsJsonAsync("refund", body, ct);
        var json = await ReadEnvelopeAsync(res, "refund", ct);

        var data = json.GetProperty("data");
        var id = data.TryGetProperty("id", out var i) ? i.ToString() : transactionReference;
        var status = data.TryGetProperty("status", out var st) ? st.GetString() : null;

        // Paystack settles refunds over hours or days — "processing" is the normal
        // immediate answer, not a failure.
        var mapped = status is "processed" or "success"
            ? PayoutStatus.Refunded
            : status is "failed"
                ? PayoutStatus.Failed
                : PayoutStatus.Processing;

        return new PaystackRefundResult(id, mapped,
            json.TryGetProperty("message", out var m) ? m.GetString() : null);
    }

    /// <summary>Paystack transfer states → our payout states.</summary>
    private static PayoutStatus MapTransferStatus(string? status) => status switch
    {
        "success" => PayoutStatus.Paid,
        "failed" or "reversed" => PayoutStatus.Failed,
        // "pending", "otp", "processing" all mean in-flight.
        _ => PayoutStatus.Processing,
    };

    public bool IsValidWebhookSignature(string rawBody, string? signatureHeader)
    {
        // Without a key there is nothing to verify against; simulation mode accepts
        // unsigned webhooks so the flow is testable locally (Development only).
        if (string.IsNullOrWhiteSpace(_opt.SecretKey)) return _simulate;
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_opt.SecretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader.Trim().ToLowerInvariant()));
    }

    // ---- helpers ----

    private async Task<JsonElement> ReadEnvelopeAsync(HttpResponseMessage res, string op, CancellationToken ct)
    {
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Paystack {Op} failed ({Status}): {Body}", op, (int)res.StatusCode, raw);
            throw new PaystackException($"Paystack {op} failed ({(int)res.StatusCode}).");
        }

        var json = JsonDocument.Parse(raw).RootElement;
        if (!json.TryGetProperty("status", out var ok) || !ok.GetBoolean())
        {
            var msg = json.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
            _log.LogError("Paystack {Op} rejected: {Message}", op, msg);
            throw new PaystackException($"Paystack {op} rejected: {msg}");
        }

        return json;
    }

    /// <summary>Paystack channel names; MoMo goes through "mobile_money" in Ghana.</summary>
    private static string[] ChannelsFor(PaymentChannel channel) => channel switch
    {
        PaymentChannel.Card => ["card"],
        _ => ["mobile_money"]
    };

    private static Dictionary<string, object?> BuildMetadata(
        PaymentChannel channel, string? phone, IDictionary<string, string>? extra)
    {
        var meta = new Dictionary<string, object?>
        {
            ["channel"] = channel.ToString(),
            ["phone"] = phone
        };
        if (extra is not null)
            foreach (var (k, v) in extra) meta[k] = v;
        return meta;
    }

    private static PaymentStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "success" => PaymentStatus.Success,
        "failed" or "reversed" => PaymentStatus.Failed,
        "abandoned" => PaymentStatus.Abandoned,
        _ => PaymentStatus.Initialized      // "ongoing", "pending", "queued", null…
    };

    /// <summary>
    /// Paystack reports the settled channel plus (for MoMo) the network in the
    /// authorization block; we only need the coarse channel here.
    /// </summary>
    private static PaymentChannel MapChannel(string? channel) => channel?.ToLowerInvariant() switch
    {
        "card" => PaymentChannel.Card,
        _ => PaymentChannel.MomoMtn
    };

    internal static string NewReference() =>
        "medan_" + Guid.NewGuid().ToString("N")[..16].ToLower(CultureInfo.InvariantCulture);
}

/// <summary>Raised when Paystack returns a non-2xx or a <c>status:false</c> envelope.</summary>
public class PaystackException : Exception
{
    public PaystackException(string message) : base(message) { }
}
