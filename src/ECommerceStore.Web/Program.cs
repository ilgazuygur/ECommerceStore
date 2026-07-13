using ECommerceStore.Web.Data;
using ECommerceStore.Web.Data.Seed;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Areas.Admin;
using ECommerceStore.Web.Services.Admin;
using ECommerceStore.Web.Services.AI;
using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Catalog;
using ECommerceStore.Web.Services.Cart;
using ECommerceStore.Web.Services.Checkout;
using ECommerceStore.Web.Services.Email;
using ECommerceStore.Web.Services.Invoices;
using ECommerceStore.Web.Services.Images;
using ECommerceStore.Web.Services.Orders;
using ECommerceStore.Web.Services.Payments;
using ECommerceStore.Web.Services.Assistant;
using ECommerceStore.Web.Services.Assistant.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Text.Json;
using StoreConfiguration = ECommerceStore.Web.Services.Common.StoreOptions;

var builder = WebApplication.CreateBuilder(args);

var connectionStringBuilder = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required."));

if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
{
    connectionStringBuilder.DataSource = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, connectionStringBuilder.DataSource));
}

var databaseDirectory = Path.GetDirectoryName(connectionStringBuilder.DataSource);
if (!string.IsNullOrWhiteSpace(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionStringBuilder.ToString(), sqlite => sqlite.CommandTimeout(15)));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<EnabledUserCookieEvents>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
    options.Cookie.Name = "ECommerceStore.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.EventsType = typeof(EnabledUserCookieEvents);
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "ECommerceStore.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

builder.Services.AddOptions<StoreConfiguration>().BindConfiguration(StoreConfiguration.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SmtpOptions>().BindConfiguration(SmtpOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<UploadOptions>().BindConfiguration(UploadOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AIServiceOptions>().BindConfiguration(AIServiceOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AssistantOptions>().BindConfiguration(AssistantOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => options.UsesMock || options.IsRealProviderComplete,
        "A real assistant provider requires a safe absolute BaseUrl, Model, and ApiKey from user secrets or environment variables.")
    .ValidateOnStart();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IOrderNumberGenerator, OrderNumberGenerator>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IProductQueryService, ProductQueryService>();
builder.Services.AddScoped<IOrderQueryService, OrderQueryService>();
builder.Services.AddScoped<ICartCalculator, CartCalculator>();
builder.Services.AddScoped<IAnonymousCartStore, AnonymousCartStore>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICartMergeService, CartMergeService>();
builder.Services.AddSingleton<IEmailComposer, EmailComposer>();
builder.Services.AddScoped<SmtpEmailTransport>();
builder.Services.AddScoped<DevelopmentFileEmailTransport>();
builder.Services.AddScoped<IEmailService, ResilientEmailService>();
builder.Services.AddScoped<IPaymentService, FakePaymentService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddSingleton<IInvoicePdfService, InvoicePdfService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<IAIService, MockAIService>();
builder.Services.AddSingleton<IProductImageService, ProductImageService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminProductService, AdminProductService>();
builder.Services.AddScoped<IAdminCategoryService, AdminCategoryService>();
builder.Services.AddScoped<IAdminOrderService, AdminOrderService>();
builder.Services.AddScoped<IAdminCustomerService, AdminCustomerService>();
builder.Services.AddScoped<IOrderConfirmationDispatcher, OrderConfirmationDispatcher>();
builder.Services.AddScoped<IProductAssistantToolService, ProductAssistantToolService>();
builder.Services.AddScoped<IShoppingAssistantService, ShoppingAssistantService>();
builder.Services.AddSingleton<MockAssistantAiClient>();
builder.Services.AddHttpClient<OpenAiCompatibleAssistantClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddScoped<IAssistantAiClient>(services =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AssistantOptions>>().Value;
    return options.UsesMock
        ? services.GetRequiredService<MockAssistantAiClient>()
        : services.GetRequiredService<OpenAiCompatibleAssistantClient>();
});

builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("assistant", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many assistant requests",
            Detail = "Wait a moment before trying again."
        }), token);
    };
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Conventions.Add(new AdminAreaAuthorizationConvention());
});

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Security response headers on every response, including static files. The
// Content-Security-Policy allows only same-origin scripts (no inline script),
// permits external HTTPS product images, and forbids framing. Bootstrap sets
// element styles via JavaScript, so inline styles are allowed but scripts are not.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' https: data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "form-action 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'";
    await next();
});

var storeCulture = CultureInfo.GetCultureInfo("en-US");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(storeCulture),
    SupportedCultures = [storeCulture],
    SupportedUICultures = [storeCulture]
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/status/{0}");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

await ApplicationDbInitializer.SeedAsync(app.Services, app.Environment);

app.Run();

public partial class Program;
