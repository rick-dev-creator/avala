using Avala.Server.Activity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal static class Columns
{
    public const int TokenPrecision = 30;

    public const int AmountPrecision = 28;

    public const int EnumLength = 30;

    public static void MapTokens<TOwner>(OwnedNavigationBuilder<TOwner, TokenTotals> tokens)
        where TOwner : class
    {
        tokens.Property(total => total.Input).HasColumnName("input_tokens").HasPrecision(TokenPrecision, 0);
        tokens.Property(total => total.Output).HasColumnName("output_tokens").HasPrecision(TokenPrecision, 0);
        tokens.Property(total => total.CacheRead).HasColumnName("cache_read_tokens").HasPrecision(TokenPrecision, 0);
        tokens.Property(total => total.CacheWrite).HasColumnName("cache_write_tokens").HasPrecision(TokenPrecision, 0);
        tokens.Property(total => total.Reasoning).HasColumnName("reasoning_tokens").HasPrecision(TokenPrecision, 0);
    }

    public static void MapCosts<TOwner>(OwnedNavigationBuilder<TOwner, CostTotal> costs, string table, params string[] owner)
        where TOwner : class
    {
        costs.ToTable(table);
        costs.WithOwner().HasForeignKey(owner);
        string[] key = [.. owner, nameof(CostTotal.Currency), nameof(CostTotal.Basis)];
        costs.HasKey(key);
        costs.Property(cost => cost.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
        costs.Property(cost => cost.Basis).HasColumnName("basis").HasConversion<string>().HasMaxLength(EnumLength);
        costs.Property(cost => cost.Amount).HasColumnName("amount").HasPrecision(AmountPrecision, CostAmount.Decimals);
    }
}
