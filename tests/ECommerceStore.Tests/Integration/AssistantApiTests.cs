using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Data.Seed;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Services.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ECommerceStore.Tests.Integration;

public sealed class AssistantApiTests
{
    [Fact]
    public async Task Anonymous_api_get_returns_json_401_without_login_redirect()
    {
        await WithFactoryAsync(async factory =>
        {
            using var client = CreateClient(factory);
            var response = await client.GetAsync("/api/assistant/conversations");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        });
    }

    [Fact]
    public async Task Storefront_renders_widget_and_anonymous_sign_in_state()
    {
        await WithFactoryAsync(async factory =>
        {
            using var client = CreateClient(factory);
            var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
            Assert.Contains("data-assistant-widget", html, StringComparison.Ordinal);
            Assert.Contains("data-authenticated=\"false\"", html, StringComparison.Ordinal);
            Assert.Contains("/js/assistant.js", html, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Authenticated_post_requires_antiforgery_and_happy_path_returns_grounded_cards()
    {
        await WithFactoryAsync(async factory =>
        {
            using var client = CreateClient(factory);
            await SignInAsync(client, ApplicationDbInitializer.DevelopmentAdminEmail, ApplicationDbInitializer.DevelopmentAdminPassword);

            var rejected = await client.PostAsync("/api/assistant/conversations", JsonContent("{}"));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            var token = await GetTokenAsync(client);
            using var create = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/conversations") { Content = JsonContent("{}") };
            create.Headers.Add("RequestVerificationToken", token);
            var created = await client.SendAsync(create);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var conversationId = createdJson.RootElement.GetProperty("id").GetGuid();

            var body = JsonSerializer.Serialize(new { clientRequestId = Guid.NewGuid(), message = "Show electronics under $100" });
            using var send = new HttpRequestMessage(HttpMethod.Post, $"/api/assistant/conversations/{conversationId}/messages") { Content = JsonContent(body) };
            send.Headers.Add("RequestVerificationToken", token);
            var response = await client.SendAsync(send);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("productUrl", json, StringComparison.Ordinal);
            Assert.Contains("Electronics", json, StringComparison.Ordinal);
            Assert.DoesNotContain("FullDescription", json, StringComparison.Ordinal);

            var longBody = JsonSerializer.Serialize(new { clientRequestId = Guid.NewGuid(), message = new string('x', 2001) });
            using var tooLong = new HttpRequestMessage(HttpMethod.Post, $"/api/assistant/conversations/{conversationId}/messages") { Content = JsonContent(longBody) };
            tooLong.Headers.Add("RequestVerificationToken", token);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(tooLong)).StatusCode);
        });
    }

    [Fact]
    public async Task Foreign_owned_conversation_is_hidden_as_404()
    {
        await WithFactoryAsync(async factory =>
        {
            using var owner = CreateClient(factory);
            await SignInAsync(owner, ApplicationDbInitializer.DevelopmentAdminEmail, ApplicationDbInitializer.DevelopmentAdminPassword);
            var ownerToken = await GetTokenAsync(owner);
            using var create = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/conversations") { Content = JsonContent("{}") };
            create.Headers.Add("RequestVerificationToken", ownerToken);
            using var createdJson = JsonDocument.Parse(await (await owner.SendAsync(create)).Content.ReadAsStringAsync());
            var conversationId = createdJson.RootElement.GetProperty("id").GetGuid();

            await CreateCustomerAsync(factory.Services);
            using var foreign = CreateClient(factory);
            await SignInAsync(foreign, "assistant-customer@test.local", "Customer123!");
            var response = await foreign.GetAsync($"/api/assistant/conversations/{conversationId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        });
    }

    [Fact]
    public async Task Disabled_user_cookie_is_rejected_with_json_401()
    {
        await WithFactoryAsync(async factory =>
        {
            await CreateCustomerAsync(factory.Services);
            using var client = CreateClient(factory);
            await SignInAsync(client, "assistant-customer@test.local", "Customer123!");
            using (var scope = factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByEmailAsync("assistant-customer@test.local");
                Assert.NotNull(user);
                user!.IsEnabled = false;
                Assert.True((await users.UpdateAsync(user)).Succeeded);
            }

            var response = await client.GetAsync("/api/assistant/conversations");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        });
    }

    [Fact]
    public async Task Per_user_rate_limit_returns_problem_json_429()
    {
        await WithFactoryAsync(async factory =>
        {
            using var client = CreateClient(factory);
            await SignInAsync(client, ApplicationDbInitializer.DevelopmentAdminEmail, ApplicationDbInitializer.DevelopmentAdminPassword);
            for (var request = 0; request < 60; request++)
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/assistant/conversations")).StatusCode);

            var rejected = await client.GetAsync("/api/assistant/conversations");
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        });
    }

    private static async Task WithFactoryAsync(Func<AssistantWebApplicationFactory, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ecommerce-assistant-http-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path}").Options;
            await using (var db = new ApplicationDbContext(options)) await db.Database.MigrateAsync();
            using var factory = new AssistantWebApplicationFactory(path);
            await test(factory);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
                try { File.Delete(path + suffix); } catch (IOException) { }
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var page = await client.GetAsync("/account/login");
        var token = ExtractToken(await page.Content.ReadAsStringAsync());
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = password, ["RememberMe"] = "false", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> GetTokenAsync(HttpClient client) => ExtractToken(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
    private static string ExtractToken(string html)
    {
        var value = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value;
    }
    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task CreateCustomerAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = "assistant-customer@test.local", Email = "assistant-customer@test.local", EmailConfirmed = true,
            FirstName = "Assistant", LastName = "Customer", IsEnabled = true
        };
        Assert.True((await users.CreateAsync(user, "Customer123!")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.Customer)).Succeeded);
    }

    private sealed class AssistantWebApplicationFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}",
                    ["Assistant:Provider"] = "Mock"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            });
        }
    }
}
