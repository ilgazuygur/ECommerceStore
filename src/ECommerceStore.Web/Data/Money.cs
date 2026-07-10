using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ECommerceStore.Web.Data;

public static class Money
{
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static long ToMinorUnits(decimal value)
    {
        if (value < 0 || value > MaxAmount || value != Round(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Money must be non-negative, within decimal(18,2), and have at most two fractional digits.");
        }

        return checked((long)(value * 100m));
    }

    public static decimal FromMinorUnits(long value) => value / 100m;
}

public sealed class MoneyConverter : ValueConverter<decimal, long>
{
    public static readonly MoneyConverter Instance = new();

    private MoneyConverter()
        : base(value => Money.ToMinorUnits(value), value => Money.FromMinorUnits(value))
    {
    }
}

public sealed class NullableMoneyConverter : ValueConverter<decimal?, long?>
{
    private static readonly Expression<Func<decimal?, long?>> ToProviderExpression =
        value => value.HasValue ? Money.ToMinorUnits(value.Value) : null;

    private static readonly Expression<Func<long?, decimal?>> FromProviderExpression =
        value => value.HasValue ? Money.FromMinorUnits(value.Value) : null;

    public static readonly NullableMoneyConverter Instance = new();

    private NullableMoneyConverter()
        : base(ToProviderExpression, FromProviderExpression)
    {
    }
}

public static class MoneyComparers
{
    public static readonly ValueComparer<decimal> Decimal = new(
        (left, right) => left == right,
        value => value.GetHashCode(),
        value => value);

    public static readonly ValueComparer<decimal?> NullableDecimal = new(
        (left, right) => left == right,
        value => value.GetHashCode(),
        value => value);
}
