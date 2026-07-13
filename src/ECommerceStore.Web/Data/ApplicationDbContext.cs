using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Assistant;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Models.Orders;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ECommerceStore.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ShoppingCart> Carts => Set<ShoppingCart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CheckoutAttempt> CheckoutAttempts => Set<CheckoutAttempt>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatMessageProduct> ChatMessageProducts => Set<ChatMessageProduct>();
    public DbSet<AssistantRequest> AssistantRequests => Set<AssistantRequest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditValues();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditValues();
        return base.SaveChanges();
    }

    private void ApplyAuditValues()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                SetCreatedAndUpdated(entry.Entity, now);
            }
            else if (entry.State == EntityState.Modified)
            {
                SetUpdatedAndVersion(entry.Entity, now);
            }
        }
    }

    private static void SetCreatedAndUpdated(object entity, DateTime now)
    {
        switch (entity)
        {
            case ApplicationUser user:
                user.CreatedAtUtc = user.CreatedAtUtc == default ? now : user.CreatedAtUtc;
                user.UpdatedAtUtc = now;
                break;
            case Category category:
                category.CreatedAtUtc = category.CreatedAtUtc == default ? now : category.CreatedAtUtc;
                category.UpdatedAtUtc = now;
                break;
            case Product product:
                product.CreatedAtUtc = product.CreatedAtUtc == default ? now : product.CreatedAtUtc;
                product.UpdatedAtUtc = now;
                break;
            case ShoppingCart cart:
                cart.CreatedAtUtc = cart.CreatedAtUtc == default ? now : cart.CreatedAtUtc;
                cart.UpdatedAtUtc = now;
                break;
            case CartItem item:
                item.CreatedAtUtc = item.CreatedAtUtc == default ? now : item.CreatedAtUtc;
                item.UpdatedAtUtc = now;
                break;
            case CheckoutAttempt attempt:
                attempt.CreatedAtUtc = attempt.CreatedAtUtc == default ? now : attempt.CreatedAtUtc;
                attempt.UpdatedAtUtc = now;
                break;
            case Order order:
                order.CreatedAtUtc = order.CreatedAtUtc == default ? now : order.CreatedAtUtc;
                order.UpdatedAtUtc = now;
                break;
            case ChatConversation conversation:
                conversation.CreatedAtUtc = conversation.CreatedAtUtc == default ? now : conversation.CreatedAtUtc;
                conversation.UpdatedAtUtc = now;
                break;
            case ChatMessage message:
                message.CreatedAtUtc = message.CreatedAtUtc == default ? now : message.CreatedAtUtc;
                break;
            case AssistantRequest request:
                request.CreatedAtUtc = request.CreatedAtUtc == default ? now : request.CreatedAtUtc;
                request.UpdatedAtUtc = now;
                break;
        }
    }

    private static void SetUpdatedAndVersion(object entity, DateTime now)
    {
        switch (entity)
        {
            case ApplicationUser user:
                user.UpdatedAtUtc = now;
                break;
            case Category category:
                category.UpdatedAtUtc = now;
                category.Version++;
                break;
            case Product product:
                product.UpdatedAtUtc = now;
                product.Version++;
                break;
            case ShoppingCart cart:
                cart.UpdatedAtUtc = now;
                cart.Version++;
                break;
            case CartItem item:
                item.UpdatedAtUtc = now;
                break;
            case CheckoutAttempt attempt:
                attempt.UpdatedAtUtc = now;
                attempt.Version++;
                break;
            case Order order:
                order.UpdatedAtUtc = now;
                order.Version++;
                break;
            case ChatConversation conversation:
                conversation.UpdatedAtUtc = now;
                break;
            case AssistantRequest request:
                request.UpdatedAtUtc = now;
                break;
        }
    }
}
