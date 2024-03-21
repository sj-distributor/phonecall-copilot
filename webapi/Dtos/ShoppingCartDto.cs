// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace CopilotChat.WebApi.Dtos;

public class ShoppingCartDto
{
    public ShoppingCartDto()
    {
        ShoppingCartItems = new List<CombinedShoppingCartItemDto>();
    }

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid MerchId { get; set; }

    public string MerchName { get; set; }

    public string MerchLogo { get; set; }

    public MerchType MerchType { get; set; }

    public DeliveryType MerchDeliveryType { get; set; }

    public ShoppingCartType ShoppingCartType { get; set; }

    public decimal MerchMinimumAmountForDelivery { get; set; }

    public decimal? MerchMinimumAmountToVoidDeliveryFee { get; set; }

    public bool MerchIsInBusinessHours { get; set; }

    public bool MerchIsWithinPolygons { get; set; }
    public bool MerchIsRemainingStockAllPurchasable { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public DateTimeOffset? DateLastAdded =>
        ShoppingCartItems.OrderByDescending(x => x.CreatedDate).FirstOrDefault()?.CreatedDate;

    public bool CanCheckout { get; set; }

    public ShoppingCartCannotCheckoutReason? CannotCheckoutReason { get; set; }

    public List<CombinedShoppingCartItemDto> ShoppingCartItems { get; set; }

    public decimal HowMuchMoreToGetFreeDelivery { get; set; }
    public bool IsPromotionOnlyMerch { get; set; }

    public bool ShowSales { get; set; }

    public int MaxPreorderDays { get; set; }

    public bool AcceptPreorder { get; set; }

    public decimal CartTotal
    {
        get
        {
            return ShoppingCartItems.Sum(x => x.Subtotal);
        }
    }

    public decimal CartTotalExcludeUnavailableItems
    {
        get
        {
            return GetExcludeUnavailableShoppingCartItems().Sum(x => x.Subtotal);
        }
    }

    public List<CombinedShoppingCartItemDto> GetExcludeUnavailableShoppingCartItems()
    {
        return ShoppingCartItems
            .Where(x => !x.Unavailable)
            .Where(x => x.ShoppingCartItemQuantityStatus == ShoppingCartItemQuantityStatus.Normal)
            .Where(x => x.ShoppingCartItemParamsStatus == ShoppingCartItemParamsStatus.Normal ||
                        x.ShoppingCartItemParamsStatus == ShoppingCartItemParamsStatus.ParameterOptional)
            .ToList();
    }

    public CombinedShoppingCartItemDto GetShoppingCartMaxProductExchangeItem(List<Guid> productExchangeFoodIds)
    {
        return ShoppingCartItems
            .Where(x => !x.Unavailable)
            .Where(x => x.ShoppingCartItemQuantityStatus == ShoppingCartItemQuantityStatus.Normal)
            .Where(x => productExchangeFoodIds.Contains(x.FoodId))
            .Where(x => x.ShoppingCartItemParamsStatus == ShoppingCartItemParamsStatus.Normal ||
                        x.ShoppingCartItemParamsStatus == ShoppingCartItemParamsStatus.ParameterOptional)
            .OrderByDescending(x => x.DiscountPrice > 0 ? x.DiscountPrice : x.Price).FirstOrDefault();
    }

    public decimal GetShoppingCartMaxProductExchangePrice(List<Guid> productExchangeFoodIds) =>
        GetShoppingCartMaxProductExchangeItem(productExchangeFoodIds)?.Price ?? 0;
}

public class ShoppingCartItemDto
{
    public ShoppingCartItemDto()
    {
        ShoppingCartItemParams = new List<ShoppingCartItemParamDto>();
    }

    public Guid Id { get; set; }
    public Guid ShoppingCartId { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public Guid FoodId { get; set; }
    public string ExternalId { get; set; }
    public string FoodName { get; set; }

    public int FoodStock { get; set; }

    public string FoodBanner { get; set; }

    public decimal FoodOriginalPrice { get; set; }

    public int FoodLimitPerOrder { get; set; }

    public int? FoodMinimumPurchaseQuantity { get; set; }

    public int FoodMinimumPreOrderDays { get; set; }

    public MerchFoodStatus FoodStatus { get; set; }

    public DeliveryType DeliveryType { get; set; }


    public IEnumerable<DeliveryType> DeliveryTypes { get; set; }

    public bool FoodIsInBusinessHours { get; set; }

    public bool FoodIsDel { get; set; }

    public bool FoodIsAvailable { get; set; }

    public int Quantity { get; set; }

    public int TotalQuantityInCart { get; set; }

    public decimal Price { get; set; }

    public decimal PriceOnAdded { get; set; }

    public decimal DiscountPrice { get; set; }

    public decimal CutPriceDiscountPercentage { get; set; }

    public decimal FollowBuyDiscountPercentageLabel { get; set; }

    public ShoppingCartItemParamsStatus ShoppingCartItemParamsStatus { get; set; }

    public ShoppingCartItemQuantityStatus ShoppingCartItemQuantityStatus { get; set; }

    public bool Unavailable => UnavailableReason != ShoppingCartItemUnavailableReason.None;

    public ShoppingCartItemUnavailableReason UnavailableReason { get; set; }

    public List<ShoppingCartItemParamDto> ShoppingCartItemParams { get; set; }

    public decimal Subtotal
    {
        get
        {
            return ((DiscountPrice > 0 ? DiscountPrice : Price) +
                    ShoppingCartItemParams.Sum(p => p.Price * p.Quantity)) * Quantity;
        }
    }

    public bool CanAdd { get; set; }

    public string CannotAddReason { get; set; }

    public ShoppingCartItemCannotAddReason CannotAddReasonEnum { get; set; }

    public int GetParamsSaleToCalculateUnitQtyConversion()
    {
        if (!ShoppingCartItemParams.Any()) return 1;

        var greaterThanZeroParams =
            ShoppingCartItemParams.Where(x => x.SaleToCalculateUnitQtyConversion > 0).ToList();

        if (greaterThanZeroParams.Any())
            return greaterThanZeroParams
                .OrderByDescending(x => x.SaleToCalculateUnitQtyConversion)
                .First().SaleToCalculateUnitQtyConversion;
        return 1;
    }

    public bool IsInventoryAlert { get; set; }

    public bool NotMatchDeliveryType { get; set; }

    public DateTimeOffset? EarliestDeliveryTime { get; set; }

    public bool IsPresale { get; set; }

    public int SoldIn30Days { get; set; }

    public DateTimeOffset? ExpireDate { get; set; }

    public bool HasFreeItem { get; set; }
}

public class CombinedShoppingCartItemDto : ShoppingCartItemDto
{
    public bool IsKocPromoFood { get; set; }

    public decimal Subtotal
    {
        get
        {
            if (IsKocPromoFood)
            {
                return (DiscountPrice + ShoppingCartItemParams.Sum(p => p.Price * p.Quantity)) * Quantity;
            }

            return ((DiscountPrice > 0 ? DiscountPrice : Price) +
                    ShoppingCartItemParams.Sum(p => p.Price * p.Quantity)) * Quantity;
        }
    }
}

public class ShoppingCartItemParamDto
{
    public Guid Id { get; set; }

    public Guid ShoppingCartItemId { get; set; }

    public Guid ParameterGroupId { get; set; }

    public Guid ParameterItemId { get; set; }

    public string Name { get; set; }

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal PriceOnAdded { get; set; }

    public int SaleToCalculateUnitQtyConversion { get; set; }

    public int Sort { get; set; }
}

public class ShoppingCartValidDto
{
    public IEnumerable<ShoppingCartMerchDto> Merchs { get; set; }
}

public class ShoppingCartMerchDto
{
    public Guid MerchId { get; set; }
    public IEnumerable<ShoppingCartMerchFoodDto> MerchFoods { get; set; }
}

public class ShoppingCartMerchFoodDto
{
    public Guid ValidationId => Guid.NewGuid();
    public Guid FoodId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public IEnumerable<FoodParameterDto> FoodParameters { get; set; }
    public ShoppingCartFoodInvalidType InvalidType { get; set; }
    public string InvalidDescription { get; set; }
}

public class FoodParameterForEqualDto
{
    public Guid ParameterGroupId { get; set; }

    public Guid ParameterItemId { get; set; }

    public int Quantity { get; set; }

    public override int GetHashCode()
    {
        return ParameterGroupId.GetHashCode() ^ ParameterGroupId.GetHashCode() ^ Quantity.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        if (!(obj is FoodParameterForEqualDto other))
            return false;

        return ParameterGroupId == other.ParameterGroupId && ParameterItemId == other.ParameterItemId &&
               Quantity == other.Quantity;
    }
}

public enum ShoppingCartItemAddOrUpdate
{
    Add,
    Update
}

public enum ShoppingCartItemParamsStatus
{
    Normal,
    ParameterOptional,
    ParameterInvalid,
    ParameterCleared,
    ParameterRequired
}

public enum ShoppingCartItemQuantityStatus
{
    Normal,
    OutOfStock,
    LessThanMinimumPurchaseQuantity,
    GreaterThanLimitPerOrder
}

public enum ShoppingCartFoodInvalidType
{
    Normal,
    NotAvailable,
    OutOfStock,
    ItemChanged,
    ParameterMissing,
    MinimumPurchaseQuantityRequired,
    LimitPurchaseQuantity,
    PriceChanged,
    NotInBusinessHours
}

public enum ShoppingCartItemUnavailableReason
{
    None,
    MerchantUnavailable,
    FoodUnavailable,
    OutOfDeliveryPolygons,
    FoodOffSale,
    FoodStockIsZero
}

public enum ShoppingCartCannotCheckoutReason
{
    Closed,
    NotInBusinessHours
}

public enum ShoppingCartItemCannotAddReason
{
    None,
    OutOfStock,
    LimitPerOrder
}

public enum ShoppingCartType
{
    Generic,
    DineIn
}

public enum MerchType
{
    [Description("餐厅")] Restaurant,
    [Description("超市")] Shop,
    [Description("百货")] Retail,

    [Description("系统类型 yesmeal point ...")]
    Platform
}
