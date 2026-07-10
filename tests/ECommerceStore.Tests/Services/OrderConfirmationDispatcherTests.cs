using System.Text.Encodings.Web;
using System.Security.Claims;
using ECommerceStore.Web.Services.Email;
using ECommerceStore.Web.Services.Invoices;
using ECommerceStore.Web.Services.Orders;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECommerceStore.Tests.Services;

public sealed class OrderConfirmationDispatcherTests
{
    [Fact]
    public async Task Dispatch_sends_order_email_with_order_number()
    {
        var email = new CapturingEmailService();
        var dispatcher = Build(new StubInvoiceService(InvoicePdfServiceTests.SampleInvoice()), new InvoicePdfService(), email);

        await dispatcher.DispatchAsync("owner", "ORD-20260710-abc123");

        var message = Assert.Single(email.Sent);
        Assert.Equal("jane@example.test", message.ToAddress);
        Assert.Contains("ORD-20260710-abc123", message.Subject);
        Assert.Contains("ORD-20260710-abc123", message.TextBody);
    }

    [Fact]
    public async Task Pdf_generation_failure_does_not_throw_and_email_still_sends()
    {
        var email = new CapturingEmailService();
        var dispatcher = Build(new StubInvoiceService(InvoicePdfServiceTests.SampleInvoice()), new ThrowingPdfService(), email);

        await dispatcher.DispatchAsync("owner", "ORD-1"); // must not throw

        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task Email_failure_does_not_throw()
    {
        var dispatcher = Build(new StubInvoiceService(InvoicePdfServiceTests.SampleInvoice()), new InvoicePdfService(), new ThrowingEmailService());

        await dispatcher.DispatchAsync("owner", "ORD-1"); // must not throw
    }

    [Fact]
    public async Task Missing_invoice_is_a_no_op()
    {
        var email = new CapturingEmailService();
        var dispatcher = Build(new StubInvoiceService(null), new InvoicePdfService(), email);

        await dispatcher.DispatchAsync("owner", "ORD-1");

        Assert.Empty(email.Sent);
    }

    private static OrderConfirmationDispatcher Build(IInvoiceService invoice, IInvoicePdfService pdf, IEmailService email) =>
        new(invoice, pdf, new EmailComposer(HtmlEncoder.Default), email, NullLogger<OrderConfirmationDispatcher>.Instance);

    private sealed class StubInvoiceService(InvoiceModel? invoice) : IInvoiceService
    {
        public Task<InvoiceModel?> GetInvoiceAsync(ClaimsPrincipal user, string orderNumber, CancellationToken cancellationToken = default) => Task.FromResult(invoice);
        public Task<InvoiceModel?> GetInvoiceAsync(string userId, string orderNumber, CancellationToken cancellationToken = default) => Task.FromResult(invoice);
    }

    private sealed class CapturingEmailService : IEmailService
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(new EmailDeliveryResult(true, "Test", "Sent"));
        }
    }

    private sealed class ThrowingEmailService : IEmailService
    {
        public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("smtp down");
    }

    private sealed class ThrowingPdfService : IInvoicePdfService
    {
        public Task<byte[]> GenerateAsync(InvoiceModel invoice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("pdf failed");
    }
}
