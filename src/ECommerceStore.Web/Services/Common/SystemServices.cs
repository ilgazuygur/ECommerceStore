namespace ECommerceStore.Web.Services.Common;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public interface IOrderNumberGenerator
{
    string CreateOrderNumber();
    string CreateInvoiceNumber();
}

public sealed class OrderNumberGenerator(IClock clock) : IOrderNumberGenerator
{
    public string CreateOrderNumber() => Create("ORD");
    public string CreateInvoiceNumber() => Create("INV");

    private string Create(string prefix) =>
        $"{prefix}-{clock.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..(prefix.Length + 1 + 8 + 1 + 16)];
}
