using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MeDan.Api.Services;

/// <summary>Bound from the "Email" configuration section.</summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    // ---- Resend (preferred) ----
    // Container hosts commonly block outbound SMTP, so Resend's HTTPS API is the
    // transport that actually delivers in production. Set ApiKey to use it.
    /// <summary>Resend API key (<c>re_…</c>). Set ⇒ mail goes over HTTPS, not SMTP.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Resend's send endpoint. Only worth changing to point tests at a stub.</summary>
    public string ApiUrl { get; set; } = "https://api.resend.com/emails";

    // ---- SMTP transport (fallback) ----
    /// <summary>SMTP host, e.g. smtp.gmail.com. Used only when <see cref="ApiKey"/> is empty.</summary>
    public string? Host { get; set; }


    /// <summary>Must be a STARTTLS port (587/25). 465 is implicit TLS, which SmtpClient cannot speak.</summary>
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }

    /// <summary>Give up rather than hold the request open on a silently-blocked port.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// From address; defaults to <see cref="User"/> when empty. With Resend its
    /// domain must be verified in the dashboard, or every send is rejected.
    /// </summary>
    public string? From { get; set; }
    public string FromName { get; set; } = "MeDan";
}

/// <summary>
/// Sends transactional email through Resend's HTTPS API when an API key is set,
/// falling back to SMTP otherwise.
///
/// Unlike <see cref="PushSender"/>, delivery here is **not** best-effort: an OTP
/// that never arrives leaves a user stranded on the verification screen with no
/// way forward, so every send reports whether it succeeded and callers are
/// expected to act on a false. Errors are still logged with enough detail to
/// tell a bad credential from a blocked port.
///
/// With no transport configured at all, Development logs the message (the OTP
/// shows up in the API console, keeping local sign-up testable) and counts it as
/// delivered; every other environment treats it as a failure, matching how the
/// Paystack key is handled.
/// </summary>
public class EmailSender
{
    /// <summary>Retries past a rate-limit answer; beyond this the send is reported failed.</summary>
    private const int MaxSendAttempts = 2;

    private readonly EmailOptions _opt;
    private readonly HttpClient _http;
    private readonly IHostEnvironment _env;
    private readonly ILogger<EmailSender> _log;

    public EmailSender(
        IOptions<EmailOptions> opt,
        HttpClient http,
        IHostEnvironment env,
        ILogger<EmailSender> log)
    {
        _opt = opt.Value;
        _http = http;
        _env = env;
        _log = log;
    }

    // Resend wins when both are filled in. That precedence is easy to forget and
    // silent when wrong — leave a stale ApiKey behind and SMTP never runs — so
    // startup announces the transport it actually picked.
    private bool UsesHttp => !string.IsNullOrWhiteSpace(_opt.ApiKey);
    private bool UsesSmtp => !string.IsNullOrWhiteSpace(_opt.Host);

    /// <summary>False when neither transport is set up — nothing can be sent.</summary>
    public bool IsConfigured => UsesHttp || UsesSmtp;

    /// <summary>The transport in use, for the startup log.</summary>
    public string TransportDescription =>
        UsesHttp ? $"Resend ({_opt.ApiUrl}) as {FromAddress ?? "<no From set>"}"
        : UsesSmtp ? $"SMTP ({_opt.Host}:{_opt.Port}) as {FromAddress ?? "<no From set>"}"
        : "none — sending is disabled";

    /// <summary>True when both are configured, so the unused one can be called out.</summary>
    public bool HasUnusedSmtpConfig => UsesHttp && UsesSmtp;

    /// <summary>The address mail is sent from, or null when none can be determined.</summary>
    private string? FromAddress =>
        !string.IsNullOrWhiteSpace(_opt.From) ? _opt.From!.Trim()
        : !string.IsNullOrWhiteSpace(_opt.User) ? _opt.User!.Trim()
        : null;

    /// <summary>
    /// True when the message was handed to the provider without error.
    ///
    /// <paramref name="logLabel"/> is what the logs call this message. The OTP
    /// subject carries the code itself (it shows in a phone's notification
    /// preview), so it must never be the thing that gets written to a log file.
    /// </summary>
    public async Task<bool> SendAsync(
        string to, string subject, string htmlBody, string? logLabel = null, CancellationToken ct = default)
    {
        var label = logLabel ?? subject;

        if (!IsConfigured)
        {
            // Development keeps working without a mail account: the code is right
            // there in the console. Anywhere else, a silent no-op is the bug.
            if (_env.IsDevelopment())
            {
                _log.LogWarning("Email not configured — would have sent {Label} to {To}:\n{Body}",
                    label, to, htmlBody);
                return true;
            }

            _log.LogError(
                "Email not configured — cannot send {Label} to {To}. " +
                "Set Email__ApiKey (recommended) or Email__Host/User/Password.", label, to);
            return false;
        }

        var from = FromAddress;
        if (from is null)
        {
            _log.LogError("Email has no sender address — set Email__From. Nothing sent to {To}.", to);
            return false;
        }

        return UsesHttp
            ? await SendOverHttpAsync(from, to, subject, htmlBody, label, ct)
            : await SendOverSmtpAsync(from, to, subject, htmlBody, label, ct);
    }

    private async Task<bool> SendOverHttpAsync(
        string from, string to, string subject, string htmlBody, string label, CancellationToken ct)
    {
        // Serialized up front rather than with JsonContent so the request goes out
        // with a Content-Length instead of chunked — some provider front-ends
        // reject a chunked body.
        var json = JsonSerializer.Serialize(new
        {
            from = $"{_opt.FromName} <{from}>",
            to = new[] { to },
            subject,
            html = htmlBody,
        });

        // Resend allows 2 requests/second by default, which a burst of sign-ups
        // clears easily. A 429 is a "wait a moment", not a failure, so it is worth
        // one retry before telling the user we couldn't send their code.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _opt.ApiUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);

                using var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    // Resend answers {"id":"..."}; logging it lets a "they say it
                    // never arrived" question be traced in the Resend dashboard.
                    _log.LogInformation("Sent {Label} to {To}. (id {MessageId})",
                        label, to, ReadJsonString(body, "id") ?? "?");
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                    attempt <= MaxSendAttempts)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    _log.LogWarning("Resend rate-limited {Label} to {To}; retrying in {Wait}.",
                        label, to, wait);
                    await Task.Delay(wait, ct);
                    continue;
                }

                // Resend's body carries the actual reason — an unverified sending
                // domain and a bad key both 40x, and only this tells them apart.
                _log.LogError("Resend rejected {Label} to {To} ({Status} {Name}): {Message}{Hint}",
                    label, to, (int)response.StatusCode,
                    ReadJsonString(body, "name") ?? "-",
                    ReadJsonString(body, "message") ?? body,
                    HintFor(response.StatusCode));
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send {Label} to {To}.", label, to);
                return false;
            }
        }
    }

    /// <summary>Points at the setting to change, for the two mistakes that actually happen.</summary>
    private string HintFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => " — check Email__ApiKey.",
        HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity =>
            $" — is the domain of Email__From ({FromAddress}) verified in Resend?",
        _ => string.Empty,
    };

    /// <summary>One top-level string out of a provider response, or null if it isn't there.</summary>
    private static string? ReadJsonString(string body, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(property, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null; // Not JSON (a proxy's HTML error page, say) — caller falls back.
        }
    }

    private async Task<bool> SendOverSmtpAsync(
        string from, string to, string subject, string htmlBody, string label, CancellationToken ct)
    {
        if (_opt.Port == 465)
        {
            // SmtpClient only does STARTTLS; on 465 it hangs until the timeout.
            // Fail loudly instead of burning 20s per sign-up.
            _log.LogError(
                "SMTP port 465 (implicit TLS) is not supported — use 587, or switch to Email__ApiKey. " +
                "Nothing sent to {To}.", to);
            return false;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(from, _opt.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            message.To.Add(to);

            using var client = new SmtpClient(_opt.Host, _opt.Port)
            {
                EnableSsl = true,
                Timeout = _opt.TimeoutSeconds * 1000,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = string.IsNullOrWhiteSpace(_opt.User)
                    ? null
                    : new NetworkCredential(_opt.User, _opt.Password),
            };
            await client.SendMailAsync(message, ct);
            _log.LogInformation("Sent {Label} to {To}.", label, to);
            return true;
        }
        catch (SmtpException ex)
        {
            _log.LogError(ex,
                "SMTP refused {Label} to {To} ({StatusCode}). Check Email__User/Password " +
                "(Gmail needs an app password) and that the host allows outbound port {Port}.",
                label, to, ex.StatusCode, _opt.Port);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send {Label} to {To}.", label, to);
            return false;
        }
    }

    /// <summary>
    /// The registration/verification OTP email. The code leads the subject line so
    /// it is readable from a notification preview — which is also why the logs are
    /// given a fixed label instead.
    /// </summary>
    public Task<bool> SendOtpAsync(string to, string name, string code, CancellationToken ct = default) =>
        SendAsync(to, $"{code} is your MeDan verification code", $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:0 auto;padding:24px">
              <h2 style="color:#5B5BD6;margin-bottom:4px">MeDan</h2>
              <p>Hi {WebUtility.HtmlEncode(name)},</p>
              <p>Your verification code is:</p>
              <p style="font-size:32px;font-weight:bold;letter-spacing:8px;color:#1a1a2e;
                        background:#f4f4fb;border-radius:8px;padding:16px;text-align:center">{code}</p>
              <p>The code expires in 10 minutes. If you didn't create a MeDan account,
                 you can ignore this email.</p>
              <p style="color:#888;font-size:12px">MeDan — student hostel booking</p>
            </div>
            """, "verification code", ct);
}
