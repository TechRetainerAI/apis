using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace MeDan.Api.Services;

/// <summary>Bound from the "Email" configuration section.</summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host, e.g. smtp.gmail.com. Empty ⇒ emails are logged, not sent.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }

    /// <summary>From address; defaults to <see cref="User"/> when empty.</summary>
    public string? From { get; set; }
    public string FromName { get; set; } = "MeDan";
}

/// <summary>
/// Sends transactional email over SMTP. Mirrors <see cref="PushSender"/>'s
/// philosophy: when no SMTP host is configured the message is logged instead of
/// sent, so registration flows stay testable on a dev machine — the OTP shows
/// up in the API console. Errors are logged and swallowed; account creation
/// must not fail because a mail relay hiccupped.
/// </summary>
public class EmailSender
{
    private readonly EmailOptions _opt;
    private readonly ILogger<EmailSender> _log;

    public EmailSender(IOptions<EmailOptions> opt, ILogger<EmailSender> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.Host);

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("Email not configured — would have sent to {To}: {Subject}\n{Body}",
                to, subject, htmlBody);
            return;
        }

        try
        {
            var from = string.IsNullOrWhiteSpace(_opt.From) ? _opt.User! : _opt.From!;
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
                Credentials = new NetworkCredential(_opt.User, _opt.Password),
            };
            await client.SendMailAsync(message, ct);
            _log.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
        }
    }

    /// <summary>The registration/verification OTP email.</summary>
    public Task SendOtpAsync(string to, string name, string code, CancellationToken ct = default) =>
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
            """, ct);
}
