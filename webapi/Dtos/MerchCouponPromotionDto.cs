// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;

namespace CopilotChat.WebApi.Dtos;

public class MerchCouponPromotionDto
{
    public string Description { get; set; }

    public decimal Discount { get; set; }//如果是10，MerchCouponPromotionType=cash，

    public decimal MinimumOrderAmountToActivate { get; set; }//如果=50，Discount=10，MerchCouponPromotionType=cash，那就是满50减10，
    //如果=0，那就是无门槛使用

    public MerchCouponPromotionType CouponType { get; set; }
}


public enum MerchCouponPromotionType
{
    [Description("满减金额")]
    Cash,
    [Description("折扣百分比")]
    Percentage,
    [Description("商品兑换券")]
    ProductExchange
}
