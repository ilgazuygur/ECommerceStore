using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ECommerceStore.Web.Data.Seed;

public static class ApplicationDbInitializer
{
    public const string DevelopmentAdminEmail = "admin@localstore.test";
    public const string DevelopmentAdminPassword = "Admin123!";

    public static async Task SeedAsync(IServiceProvider services, IWebHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { RoleNames.Customer, RoleNames.Administrator })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSucceeded(result, $"create the {role} role");
            }
        }

        if (environment.IsDevelopment())
        {
            await SeedDevelopmentAdministratorAsync(userManager);
        }

        await SeedCatalogAsync(db, cancellationToken);
    }

    private static async Task SeedDevelopmentAdministratorAsync(UserManager<ApplicationUser> userManager)
    {
        var admin = await userManager.FindByEmailAsync(DevelopmentAdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                FirstName = "Development",
                LastName = "Administrator",
                Email = DevelopmentAdminEmail,
                UserName = DevelopmentAdminEmail,
                EmailConfirmed = true,
                IsEnabled = true
            };

            EnsureSucceeded(await userManager.CreateAsync(admin, DevelopmentAdminPassword), "create the Development administrator");
        }
        else
        {
            admin.FirstName = "Development";
            admin.LastName = "Administrator";
            admin.IsEnabled = true;
            admin.EmailConfirmed = true;
            EnsureSucceeded(await userManager.UpdateAsync(admin), "update the Development administrator");
        }

        if (!await userManager.IsInRoleAsync(admin, RoleNames.Administrator))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(admin, RoleNames.Administrator), "assign the Development administrator role");
        }
    }

    private static async Task SeedCatalogAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var categorySeeds = new[]
        {
            new Category { Name = "Electronics", NormalizedName = "ELECTRONICS", Slug = "electronics" },
            new Category { Name = "Home & Living", NormalizedName = "HOME & LIVING", Slug = "home-living" },
            new Category { Name = "Fitness", NormalizedName = "FITNESS", Slug = "fitness" },
            new Category { Name = "Accessories", NormalizedName = "ACCESSORIES", Slug = "accessories" }
        };

        var existingSlugs = await db.Categories.Select(category => category.Slug).ToListAsync(cancellationToken);
        var missingCategories = categorySeeds.Where(seed => !existingSlugs.Contains(seed.Slug, StringComparer.Ordinal)).ToList();
        if (missingCategories.Count > 0)
        {
            db.Categories.AddRange(missingCategories);
            await db.SaveChangesAsync(cancellationToken);
        }

        var categories = await db.Categories.ToDictionaryAsync(category => category.Slug, cancellationToken);
        var products = BuildProducts(categories);
        var existingProducts = await db.Products.Select(product => product.Slug).ToListAsync(cancellationToken);
        db.Products.AddRange(products.Where(seed => !existingProducts.Contains(seed.Slug, StringComparer.Ordinal)));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<Product> BuildProducts(IReadOnlyDictionary<string, Category> categories) =>
    [
        Product(categories, "electronics", "Aurora Wireless Headphones", "aurora-wireless-headphones", 129.00m, 99.00m, 18, true, "Immersive over-ear sound with all-day comfort.", "Enjoy detailed wireless audio, active noise reduction, and a lightweight fit designed for work, travel, and everyday listening."),
        Product(categories, "electronics", "Luma Smart Desk Lamp", "luma-smart-desk-lamp", 79.00m, null, 4, true, "A warm, adjustable light for focused spaces.", "Tune brightness and color temperature with touch controls, a USB-C charging port, and a clean aluminum profile."),
        Product(categories, "electronics", "Pocket Power Bank 20K", "pocket-power-bank-20k", 59.00m, 49.00m, 0, false, "High-capacity USB-C power for long days.", "A compact 20,000 mAh power bank with fast USB-C charging, dual-device output, and a clear battery indicator."),
        Product(categories, "home-living", "Cloud Knit Throw", "cloud-knit-throw", 68.00m, null, 25, true, "A soft textured layer for sofa or bed.", "Breathable woven cotton blend with a relaxed knit, finished edges, and an easy-care neutral tone."),
        Product(categories, "home-living", "Stoneware Pour-Over Set", "stoneware-pour-over-set", 54.00m, 44.00m, 13, false, "Hand-finished coffee ritual for two.", "A durable ceramic brewer, server, and two cups shaped for steady pouring and a balanced morning brew."),
        Product(categories, "home-living", "Cedar Entry Tray", "cedar-entry-tray", 32.00m, null, 31, false, "A natural landing place for daily essentials.", "Solid cedar tray with rounded corners and a felt base for keys, glasses, and small accessories."),
        Product(categories, "fitness", "Form Yoga Mat", "form-yoga-mat", 72.00m, 62.00m, 16, true, "Supportive grip with calm studio styling.", "A dense, non-slip exercise mat with alignment marks, cushioned support, and an easy-carry strap."),
        Product(categories, "fitness", "Pulse Resistance Set", "pulse-resistance-set", 39.00m, null, 40, false, "Five versatile resistance levels in one pouch.", "Durable fabric bands for mobility, strength, and warm-up sessions with a compact travel case."),
        Product(categories, "fitness", "Terra Insulated Bottle", "terra-insulated-bottle", 36.00m, 29.00m, 3, true, "Cold drinks, clean lines, dependable carry.", "Double-wall stainless steel bottle with a leak-resistant lid and a tactile powder-coated finish."),
        Product(categories, "accessories", "Metro Daypack", "metro-daypack", 118.00m, null, 9, true, "A streamlined pack for work and weekends.", "Water-resistant recycled shell, padded laptop sleeve, comfortable straps, and organized quick-access pockets."),
        Product(categories, "accessories", "Arc Card Wallet", "arc-card-wallet", 42.00m, 34.00m, 22, false, "Slim everyday carry in full-grain leather.", "A compact six-card wallet with a central cash slot that develops a distinctive patina over time."),
        Product(categories, "accessories", "Voyager Cable Organizer", "voyager-cable-organizer", 28.00m, null, 35, false, "Keep chargers and small tech neatly together.", "A structured zip case with flexible loops, mesh pockets, and a soft lining for travel essentials.")
    ];

    private static Product Product(
        IReadOnlyDictionary<string, Category> categories,
        string categorySlug,
        string name,
        string slug,
        decimal normalPrice,
        decimal? discountPrice,
        int stock,
        bool featured,
        string shortDescription,
        string fullDescription) => new()
        {
            CategoryId = categories[categorySlug].Id,
            Name = name,
            Slug = slug,
            NormalPrice = normalPrice,
            DiscountPrice = discountPrice,
            StockQuantity = stock,
            IsFeatured = featured,
            IsActive = true,
            ShortDescription = shortDescription,
            FullDescription = fullDescription,
            ImageKind = ProductImageKind.None
        };

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException($"Unable to {operation}: {string.Join("; ", result.Errors.Select(error => error.Code))}");
    }
}
