namespace Avala.Fixtures.Violating.ViewModels;

public sealed class BasketViewModel(ItemViewModel item, CouponViewModel coupon)
{
    public ItemViewModel Item { get; } = item;

    public CouponViewModel Coupon { get; } = coupon;
}

public sealed class ItemViewModel(Func<BasketViewModel> basket)
{
    public int Count => basket().Item == this ? 1 : 0;
}

public sealed class CouponViewModel(ItemViewModel item)
{
    public ItemViewModel Item { get; } = item;
}
