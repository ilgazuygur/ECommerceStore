using ECommerceStore.Web.Data;
using ECommerceStore.Web.Data.Seed;
using ECommerceStore.Web.Models.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace ECommerceStore.Tests.Integration;

public sealed class SeedTests
{
    [Fact]
    public async Task Development_seed_is_idempotent_and_has_required_catalog_coverage()
    {
        await using var fixture = await SeedFixture.CreateAsync("Development");

        await fixture.SeedAsync();
        await fixture.SeedAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(4, await db.Categories.CountAsync());
        Assert.Equal(12, await db.Products.CountAsync());
        Assert.True(await db.Products.CountAsync(product => product.IsFeatured) >= 3);
        Assert.Contains(await db.Products.ToListAsync(), product => product.StockQuantity is > 0 and <= 5);
        Assert.Contains(await db.Products.ToListAsync(), product => product.StockQuantity == 0);
        var admin = await userManager.FindByEmailAsync(ApplicationDbInitializer.DevelopmentAdminEmail);
        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin!, "Administrator"));
    }

    [Fact]
    public async Task Production_seed_never_creates_predictable_administrator()
    {
        await using var fixture = await SeedFixture.CreateAsync("Production");

        await fixture.SeedAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await userManager.FindByEmailAsync(ApplicationDbInitializer.DevelopmentAdminEmail));
    }

    private sealed class SeedFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly TestEnvironment _environment;

        private SeedFixture(string databasePath, ServiceProvider services, TestEnvironment environment)
        {
            _databasePath = databasePath;
            Services = services;
            _environment = environment;
        }

        public ServiceProvider Services { get; }

        public static async Task<SeedFixture> CreateAsync(string environmentName)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"ecommerce-seed-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            services.AddIdentity<ApplicationUser, IdentityRole>(options => options.User.RequireUniqueEmail = true)
                .AddEntityFrameworkStores<ApplicationDbContext>();
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            }

            return new SeedFixture(databasePath, provider, new TestEnvironment(environmentName));
        }

        public Task SeedAsync() => ApplicationDbInitializer.SeedAsync(Services, _environment);

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ECommerceStore.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
