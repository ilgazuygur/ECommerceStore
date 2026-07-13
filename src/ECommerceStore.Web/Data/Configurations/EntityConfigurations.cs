using ECommerceStore.Web.Models.Cart;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Assistant;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Models.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceStore.Web.Data.Configurations;

internal static class MoneyProperty
{
    public static PropertyBuilder<decimal> Configure(PropertyBuilder<decimal> builder, string columnName) =>
        builder.HasConversion(MoneyConverter.Instance, MoneyComparers.Decimal)
            .HasColumnName(columnName)
            .HasColumnType("INTEGER");

    public static PropertyBuilder<decimal?> Configure(PropertyBuilder<decimal?> builder, string columnName) =>
        builder.HasConversion(NullableMoneyConverter.Instance, MoneyComparers.NullableDecimal)
            .HasColumnName(columnName)
            .HasColumnType("INTEGER");
}

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.Id).HasMaxLength(450);
        builder.Property(user => user.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.LastName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(user => user.PhoneNumber).HasMaxLength(30);
        builder.Property(user => user.IsEnabled).HasDefaultValue(true);
        builder.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("EmailIndex");
        builder.HasIndex(user => new { user.IsEnabled, user.CreatedAtUtc });
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", table => table.HasCheckConstraint("CK_Categories_Version", "Version > 0"));
        builder.Property(category => category.Name).HasMaxLength(100).UseCollation("NOCASE").IsRequired();
        builder.Property(category => category.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(category => category.Slug).HasMaxLength(120).IsRequired();
        builder.Property(category => category.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.HasIndex(category => category.NormalizedName).IsUnique();
        builder.HasIndex(category => category.Slug).IsUnique();
        builder.HasIndex(category => new { category.IsActive, category.Name, category.Id });
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_NormalPrice", "NormalPriceMinor >= 0");
            table.HasCheckConstraint("CK_Products_DiscountPrice", "DiscountPriceMinor IS NULL OR (DiscountPriceMinor >= 0 AND DiscountPriceMinor < NormalPriceMinor)");
            table.HasCheckConstraint("CK_Products_Stock", "StockQuantity BETWEEN 0 AND 1000000");
            table.HasCheckConstraint("CK_Products_Image", "(ImageKind = 0 AND ImageLocation IS NULL) OR (ImageKind IN (1, 2) AND ImageLocation IS NOT NULL)");
            table.HasCheckConstraint("CK_Products_Version", "Version > 0");
        });

        builder.Property(product => product.Name).HasMaxLength(200).UseCollation("NOCASE").IsRequired();
        builder.Property(product => product.Slug).HasMaxLength(220).IsRequired();
        builder.Property(product => product.ShortDescription).HasMaxLength(500).IsRequired();
        builder.Property(product => product.FullDescription).HasMaxLength(8000).IsRequired();
        MoneyProperty.Configure(builder.Property(product => product.NormalPrice), "NormalPriceMinor");
        MoneyProperty.Configure(builder.Property(product => product.DiscountPrice), "DiscountPriceMinor");
        builder.Property(product => product.ImageKind).HasConversion<int>();
        builder.Property(product => product.ImageLocation).HasMaxLength(2048);
        builder.Property(product => product.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.HasIndex(product => product.Slug).IsUnique();
        builder.HasIndex(product => new { product.CategoryId, product.IsActive, product.Name, product.Id });
        builder.HasIndex(product => new { product.IsActive, product.IsFeatured, product.CreatedAtUtc, product.Id });
        builder.HasIndex(product => new { product.IsActive, product.StockQuantity, product.Id });
        builder.HasIndex(product => new { product.IsActive, product.NormalPrice, product.Id });
        builder.HasOne(product => product.Category)
            .WithMany(category => category.Products)
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShoppingCartConfiguration : IEntityTypeConfiguration<ShoppingCart>
{
    public void Configure(EntityTypeBuilder<ShoppingCart> builder)
    {
        builder.ToTable("Carts", table => table.HasCheckConstraint("CK_Carts_Version", "Version > 0"));
        builder.Property(cart => cart.UserId).HasMaxLength(450).IsRequired();
        builder.Property(cart => cart.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.HasIndex(cart => cart.UserId).IsUnique();
        builder.HasOne(cart => cart.User)
            .WithOne()
            .HasForeignKey<ShoppingCart>(cart => cart.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", table => table.HasCheckConstraint("CK_CartItems_Quantity", "Quantity BETWEEN 1 AND 1000000"));
        builder.HasIndex(item => new { item.CartId, item.ProductId }).IsUnique();
        builder.HasIndex(item => item.ProductId);
        builder.HasOne(item => item.Cart).WithMany(cart => cart.Items).HasForeignKey(item => item.CartId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CheckoutAttemptConfiguration : IEntityTypeConfiguration<CheckoutAttempt>
{
    public void Configure(EntityTypeBuilder<CheckoutAttempt> builder)
    {
        builder.ToTable("CheckoutAttempts", table => table.HasCheckConstraint("CK_CheckoutAttempts_Version", "Version > 0"));
        builder.Property(attempt => attempt.UserId).HasMaxLength(450).IsRequired();
        builder.Property(attempt => attempt.Status).HasConversion<int>();
        builder.Property(attempt => attempt.FailureCode).HasMaxLength(64);
        builder.Property(attempt => attempt.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.HasIndex(attempt => new { attempt.UserId, attempt.Token }).IsUnique();
        builder.HasIndex(attempt => new { attempt.Status, attempt.UpdatedAtUtc });
        builder.HasOne(attempt => attempt.User).WithMany().HasForeignKey(attempt => attempt.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_Subtotal", "SubtotalAmountMinor >= 0");
            table.HasCheckConstraint("CK_Orders_Discount", "DiscountAmountMinor >= 0 AND DiscountAmountMinor <= SubtotalAmountMinor");
            table.HasCheckConstraint("CK_Orders_Shipping", "ShippingAmountMinor >= 0");
            table.HasCheckConstraint("CK_Orders_GrandTotal", "GrandTotalMinor >= 0");
            table.HasCheckConstraint("CK_Orders_Currency", "length(CurrencyCode) = 3");
            table.HasCheckConstraint("CK_Orders_Version", "Version > 0");
        });

        builder.Property(order => order.UserId).HasMaxLength(450).IsRequired();
        builder.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(order => order.InvoiceNumber).HasMaxLength(32).IsRequired();
        builder.Property(order => order.CustomerFirstName).HasMaxLength(100).IsRequired();
        builder.Property(order => order.CustomerLastName).HasMaxLength(100).IsRequired();
        builder.Property(order => order.CustomerEmail).HasMaxLength(256).IsRequired();
        builder.Property(order => order.CustomerEmailNormalized).HasMaxLength(256).IsRequired();
        builder.Property(order => order.ShippingAddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(order => order.ShippingAddressLine2).HasMaxLength(200);
        builder.Property(order => order.ShippingCity).HasMaxLength(100).IsRequired();
        builder.Property(order => order.ShippingPostalCode).HasMaxLength(20).IsRequired();
        builder.Property(order => order.ShippingCountry).HasMaxLength(100).IsRequired();
        builder.Property(order => order.ShippingPhoneNumber).HasMaxLength(30).IsRequired();
        builder.Property(order => order.CurrencyCode).HasMaxLength(3).IsRequired();
        MoneyProperty.Configure(builder.Property(order => order.SubtotalAmount), "SubtotalAmountMinor");
        MoneyProperty.Configure(builder.Property(order => order.DiscountAmount), "DiscountAmountMinor");
        MoneyProperty.Configure(builder.Property(order => order.ShippingAmount), "ShippingAmountMinor");
        MoneyProperty.Configure(builder.Property(order => order.GrandTotal), "GrandTotalMinor");
        builder.Property(order => order.OrderStatus).HasConversion<int>();
        builder.Property(order => order.PaymentStatus).HasConversion<int>();
        builder.Property(order => order.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.HasIndex(order => order.OrderNumber).IsUnique();
        builder.HasIndex(order => order.InvoiceNumber).IsUnique();
        builder.HasIndex(order => order.CheckoutAttemptId).IsUnique();
        builder.HasIndex(order => new { order.UserId, order.CreatedAtUtc, order.Id });
        builder.HasIndex(order => new { order.OrderStatus, order.CreatedAtUtc, order.Id });
        builder.HasIndex(order => new { order.PaymentStatus, order.CreatedAtUtc, order.Id });
        builder.HasIndex(order => new { order.CustomerEmailNormalized, order.CreatedAtUtc, order.Id });
        builder.HasOne(order => order.User).WithMany().HasForeignKey(order => order.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.CheckoutAttempt).WithOne(attempt => attempt.Order).HasForeignKey<Order>(order => order.CheckoutAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "Quantity BETWEEN 1 AND 1000000");
            table.HasCheckConstraint("CK_OrderItems_Prices", "ListUnitPriceMinor >= 0 AND PaidUnitPriceMinor >= 0 AND PaidUnitPriceMinor <= ListUnitPriceMinor");
            table.HasCheckConstraint("CK_OrderItems_Totals", "DiscountAmountMinor >= 0 AND LineTotalMinor >= 0");
        });
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.ProductSlug).HasMaxLength(220).IsRequired();
        builder.Property(item => item.CategoryName).HasMaxLength(100).IsRequired();
        MoneyProperty.Configure(builder.Property(item => item.ListUnitPrice), "ListUnitPriceMinor");
        MoneyProperty.Configure(builder.Property(item => item.PaidUnitPrice), "PaidUnitPriceMinor");
        MoneyProperty.Configure(builder.Property(item => item.DiscountAmount), "DiscountAmountMinor");
        MoneyProperty.Configure(builder.Property(item => item.LineTotal), "LineTotalMinor");
        builder.HasIndex(item => item.OrderId);
        builder.HasIndex(item => item.ProductId);
        builder.HasOne(item => item.Order).WithMany(order => order.Items).HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.ToTable("PaymentRecords", table => table.HasCheckConstraint("CK_PaymentRecords_Last4", "CardLast4 IS NULL OR (length(CardLast4) = 4 AND CardLast4 NOT GLOB '*[^0-9]*')"));
        builder.Property(payment => payment.Status).HasConversion<int>();
        builder.Property(payment => payment.Provider).HasMaxLength(32).IsRequired();
        builder.Property(payment => payment.ProviderReference).HasMaxLength(64).IsRequired();
        builder.Property(payment => payment.ResultCode).HasMaxLength(64).IsRequired();
        builder.Property(payment => payment.ResultMessage).HasMaxLength(256);
        builder.Property(payment => payment.CardBrand).HasMaxLength(32);
        builder.Property(payment => payment.CardLast4).HasMaxLength(4);
        builder.HasIndex(payment => payment.CheckoutAttemptId).IsUnique();
        builder.HasIndex(payment => payment.OrderId).IsUnique();
        builder.HasIndex(payment => payment.ProviderReference).IsUnique();
        builder.HasIndex(payment => new { payment.Status, payment.ProcessedAtUtc });
        builder.HasOne(payment => payment.CheckoutAttempt).WithOne(attempt => attempt.PaymentRecord).HasForeignKey<PaymentRecord>(payment => payment.CheckoutAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(payment => payment.Order).WithOne(order => order.PaymentRecord).HasForeignKey<PaymentRecord>(payment => payment.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations", table =>
            table.HasCheckConstraint("CK_ChatConversations_LastSequence", "LastSequence >= 0"));
        builder.Property(conversation => conversation.UserId).HasMaxLength(450).IsRequired();
        builder.Property(conversation => conversation.Title).HasMaxLength(120).IsRequired();
        builder.Property(conversation => conversation.LastSequence).IsConcurrencyToken();
        builder.HasIndex(conversation => new { conversation.UserId, conversation.UpdatedAtUtc, conversation.Id });
        builder.HasOne(conversation => conversation.User).WithMany().HasForeignKey(conversation => conversation.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages", table =>
            table.HasCheckConstraint("CK_ChatMessages_Sequence", "Sequence > 0"));
        builder.Property(message => message.Role).HasConversion<int>();
        builder.Property(message => message.Content).HasMaxLength(8000).IsRequired();
        builder.HasIndex(message => new { message.ConversationId, message.Sequence }).IsUnique();
        builder.HasOne(message => message.Conversation).WithMany(conversation => conversation.Messages)
            .HasForeignKey(message => message.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatMessageProductConfiguration : IEntityTypeConfiguration<ChatMessageProduct>
{
    public void Configure(EntityTypeBuilder<ChatMessageProduct> builder)
    {
        builder.ToTable("ChatMessageProducts", table =>
            table.HasCheckConstraint("CK_ChatMessageProducts_DisplayPosition", "DisplayPosition > 0"));
        builder.HasKey(reference => new { reference.MessageId, reference.DisplayPosition });
        builder.Property(reference => reference.ProductNameSnapshot).HasMaxLength(200).IsRequired();
        MoneyProperty.Configure(builder.Property(reference => reference.PriceAtReplyTime), "PriceAtReplyTimeMinor");
        builder.HasIndex(reference => reference.ProductId);
        builder.HasOne(reference => reference.Message).WithMany(message => message.Products)
            .HasForeignKey(reference => reference.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(reference => reference.Product).WithMany().HasForeignKey(reference => reference.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AssistantRequestConfiguration : IEntityTypeConfiguration<AssistantRequest>
{
    public void Configure(EntityTypeBuilder<AssistantRequest> builder)
    {
        builder.ToTable("AssistantRequests");
        builder.Property(request => request.State).HasConversion<int>();
        builder.Property(request => request.ErrorCode).HasMaxLength(64);
        builder.HasIndex(request => new { request.ConversationId, request.ClientRequestId }).IsUnique();
        builder.HasIndex(request => new { request.State, request.UpdatedAtUtc });
        builder.HasIndex(request => request.UserMessageId).IsUnique();
        builder.HasIndex(request => request.AssistantMessageId).IsUnique();
        builder.HasOne(request => request.Conversation).WithMany(conversation => conversation.Requests)
            .HasForeignKey(request => request.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(request => request.UserMessage).WithMany().HasForeignKey(request => request.UserMessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(request => request.AssistantMessage).WithMany().HasForeignKey(request => request.AssistantMessageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
