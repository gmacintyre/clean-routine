using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Services;

public class EmailOptions
{
    /// <summary>SMTP server, e.g. "smtp.azurecomm.net" (Azure Communication Services) or your provider's.</summary>
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    /// <summary>Sender address, e.g. "DoNotReply@makeahome.app". Must be allowed by the provider.</summary>
    public string? From { get; set; }
    public string FromName { get; set; } = "Make a Home";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>Sends email over SMTP with STARTTLS, using .NET's built-in client, so any provider works
/// (Azure Communication Services, Brevo, Postmark, Mailgun, ...).</summary>
public class EmailSender(IOptions<EmailOptions> options, ILogger<EmailSender> log)
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task SendAsync(string to, string subject, string text, string html)
    {
        var o = options.Value;
        using var message = new MailMessage
        {
            From = new MailAddress(o.From!, o.FromName),
            Subject = subject,
            Body = text,
        };
        message.To.Add(to);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, null, "text/html"));

        using var client = new SmtpClient(o.SmtpHost, o.SmtpPort) { EnableSsl = true, Timeout = 20_000 };
        if (!string.IsNullOrWhiteSpace(o.SmtpUser))
            client.Credentials = new NetworkCredential(o.SmtpUser, o.SmtpPassword);
        await client.SendMailAsync(message);
        log.LogInformation("Sent \"{Subject}\" email.", subject);
    }
}
