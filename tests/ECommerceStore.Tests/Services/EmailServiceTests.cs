using System.Text.Encodings.Web;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Tests.Services;

public sealed class EmailServiceTests
{
    [Fact]
    public void Welcome_composer_encodes_untrusted_html()
    {
        var message = new EmailComposer(HtmlEncoder.Default)
            .ComposeWelcome("safe@example.test", new WelcomeEmailModel("<script>alert(1)</script>", "Test Store"));

        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.Equal("safe@example.test", message.ToAddress);
    }

    [Fact]
    public async Task Development_file_transport_creates_collision_safe_eml_without_pii_filename()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ecommerce-email-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TestEnvironment(root, "Development");
            var options = Options.Create(new SmtpOptions());
            var transport = new DevelopmentFileEmailTransport(environment, options, new FixedClock());
            var message = new EmailMessage("person@example.test", "Person", "Welcome", "Hello", "<p>Hello</p>");

            await transport.SendAsync(message);
            await transport.SendAsync(message);

            var files = Directory.GetFiles(Path.Combine(root, "App_Data", "Emails"), "*.eml");
            Assert.Equal(2, files.Length);
            Assert.Equal(2, files.Select(Path.GetFileName).Distinct().Count());
            Assert.All(files, file => Assert.DoesNotContain("person", Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
            Assert.Contains("person@example.test", await File.ReadAllTextAsync(files[0]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Production_without_smtp_fails_safely_and_does_not_write_local_email()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ecommerce-email-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TestEnvironment(root, "Production");
            var options = Options.Create(new SmtpOptions());
            var smtp = new SmtpEmailTransport(options);
            var file = new DevelopmentFileEmailTransport(environment, options, new FixedClock());
            var service = new ResilientEmailService(smtp, file, options, environment, NullLogger<ResilientEmailService>.Instance);

            var result = await service.SendAsync(new EmailMessage("person@example.test", "Person", "Welcome", "Hello", "<p>Hello</p>"));

            Assert.False(result.Succeeded);
            Assert.False(Directory.Exists(Path.Combine(root, "App_Data", "Emails")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 7, 10, 10, 11, 12, DateTimeKind.Utc);
    }

    private sealed class TestEnvironment(string contentRoot, string name) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ECommerceStore.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = name;
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
