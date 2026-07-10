using System.Net;
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

public sealed class AdminAuthorizationTests
{
    [Fact]
    public async Task Admin_area_challenges_anonymous_denies_customer_and_allows_administrator()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ecommerce-admin-http-{Guid.NewGuid():N}.db");
        try
        {
            await MigrateAsync(databasePath);
            using var factory = new AdminWebApplicationFactory(databasePath);

            using var anonymous = CreateClient(factory);
            var anonymousResponse = await anonymous.GetAsync("/Admin");
            Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
            Assert.Equal("/account/login", anonymousResponse.Headers.Location?.AbsolutePath, ignoreCase: true);

            await CreateCustomerAsync(factory.Services);
            using var customer = CreateClient(factory);
            await SignInAsync(customer, "customer@admin-http.test", "Customer123!");
            var customerResponse = await customer.GetAsync("/Admin");
            Assert.Equal(HttpStatusCode.Redirect, customerResponse.StatusCode);
            Assert.Equal("/account/access-denied", customerResponse.Headers.Location?.AbsolutePath, ignoreCase: true);

            var accessDenied = await customer.GetAsync(customerResponse.Headers.Location);
            var accessDeniedHtml = await accessDenied.Content.ReadAsStringAsync();
            var logoutToken = Regex.Match(accessDeniedHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(logoutToken));
            var logout = await customer.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = logoutToken
            }));
            Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
            var protectedAfterLogout = await customer.GetAsync("/orders");
            Assert.Equal("/account/login", protectedAfterLogout.Headers.Location?.AbsolutePath, ignoreCase: true);

            using var administrator = CreateClient(factory);
            await SignInAsync(administrator, ApplicationDbInitializer.DevelopmentAdminEmail, ApplicationDbInitializer.DevelopmentAdminPassword);
            var adminResponse = await administrator.GetAsync("/Admin");
            Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
            var html = await adminResponse.Content.ReadAsStringAsync();
            Assert.Contains("Admin · ECommerceStore", html, StringComparison.Ordinal);
            Assert.Contains("Dashboard", html, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public async Task Admin_unsafe_action_rejects_missing_antiforgery_token()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ecommerce-admin-csrf-{Guid.NewGuid():N}.db");
        try
        {
            await MigrateAsync(databasePath);
            using var factory = new AdminWebApplicationFactory(databasePath);
            using var administrator = CreateClient(factory);
            await SignInAsync(administrator, ApplicationDbInitializer.DevelopmentAdminEmail, ApplicationDbInitializer.DevelopmentAdminPassword);

            var response = await administrator.PostAsync("/Admin/Products/Deactivate/1", new FormUrlEncodedContent([]));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/account/login?returnUrl=%2FAdmin");
        loginPage.EnsureSuccessStatusCode();
        var html = await loginPage.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));

        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false",
            ["ReturnUrl"] = "/Admin",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task CreateCustomerAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = "customer@admin-http.test",
            Email = "customer@admin-http.test",
            EmailConfirmed = true,
            FirstName = "HTTP",
            LastName = "Customer",
            IsEnabled = true
        };
        var create = await users.CreateAsync(user, "Customer123!");
        Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(error => $"{error.Code}: {error.Description}")));
        var role = await users.AddToRoleAsync(user, RoleNames.Customer);
        Assert.True(role.Succeeded, string.Join("; ", role.Errors.Select(error => $"{error.Code}: {error.Description}")));
    }

    private static async Task MigrateAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
    }

    private sealed class AdminWebApplicationFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            });
        }
    }
}
