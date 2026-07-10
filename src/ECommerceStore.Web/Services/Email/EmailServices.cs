using System.Net;
using System.Text.Encodings.Web;
using ECommerceStore.Web.Services.Common;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ECommerceStore.Web.Services.Email;

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string TextBody, string HtmlBody);
public sealed record WelcomeEmailModel(string FirstName, string StoreName);
public sealed record EmailDeliveryResult(bool Succeeded, string Channel, string Code);

public interface IEmailComposer
{
    EmailMessage ComposeWelcome(string email, WelcomeEmailModel model);
}

public sealed class EmailComposer(HtmlEncoder encoder) : IEmailComposer
{
    public EmailMessage ComposeWelcome(string email, WelcomeEmailModel model)
    {
        var safeName = encoder.Encode(model.FirstName);
        var safeStore = encoder.Encode(model.StoreName);
        return new EmailMessage(
            email,
            model.FirstName,
            $"Welcome to {model.StoreName}",
            $"Hello {model.FirstName},\n\nWelcome to {model.StoreName}. Your account is ready.",
            $"<h1>Welcome to {safeStore}</h1><p>Hello {safeName},</p><p>Your account is ready.</p>");
    }
}

public interface IEmailTransport
{
    string Channel { get; }
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    private readonly SmtpOptions _options = options.Value;
    public string Channel => "SMTP";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured) throw new InvalidOperationException("SMTP is not configured.");
        var mime = CreateMimeMessage(message, _options);
        using var client = new SmtpClient();
        var security = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    internal static MimeMessage CreateMimeMessage(EmailMessage message, SmtpOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }
}

public sealed class DevelopmentFileEmailTransport(
    IWebHostEnvironment environment,
    IOptions<SmtpOptions> options,
    IClock clock) : IEmailTransport
{
    private readonly SmtpOptions _options = options.Value;
    public string Channel => "DevelopmentFile";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "Emails");
        Directory.CreateDirectory(directory);
        var fileName = $"{clock.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.eml";
        var path = Path.Combine(directory, fileName);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384, useAsync: true);
        await SmtpEmailTransport.CreateMimeMessage(message, _options).WriteToAsync(stream, cancellationToken);
    }
}

public interface IEmailService
{
    Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class ResilientEmailService(
    SmtpEmailTransport smtp,
    DevelopmentFileEmailTransport file,
    IOptions<SmtpOptions> options,
    IWebHostEnvironment environment,
    ILogger<ResilientEmailService> logger) : IEmailService
{
    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (options.Value.IsConfigured)
        {
            try
            {
                await smtp.SendAsync(message, cancellationToken);
                return new(true, smtp.Channel, "Sent");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("SMTP delivery failed with {ExceptionType}; no message content or address was logged.", exception.GetType().Name);
                if (!environment.IsDevelopment()) return new(false, smtp.Channel, "DeliveryFailed");
            }
        }

        if (!environment.IsDevelopment())
        {
            logger.LogError("Email delivery is unavailable because SMTP is not configured in Production.");
            return new(false, "Unavailable", "NotConfigured");
        }

        try
        {
            await file.SendAsync(message, cancellationToken);
            return new(true, file.Channel, "SavedLocally");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Development email fallback failed with {ExceptionType}; no message content or address was logged.", exception.GetType().Name);
            return new(false, file.Channel, "FallbackFailed");
        }
    }
}
