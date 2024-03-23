// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace CopilotChat.WebApi.Dtos;

public class MerchFoodDto
{
    public MerchFoodDto()
    {
        ParameterGroups = new List<ParameterGroupDto>();
    }

    public virtual Guid Id { get; set; }
    public Guid MerchId { get; set; }

    public string Name { get; set; }
    public int Stock { get; set; }
    public decimal Price { get; set; }
    public MerchFoodStatus Status { get; set; }
    public decimal Tax { get; set; }
    public int Like { get; set; }
    public int Sort { get; set; }
    public List<ParameterGroupDto> ParameterGroups { get; set; }

    public string Description { get; set; }

    public string ShortDescription { get; set; }
    public string Remark { get; set; }

    public decimal OriginalPrice { get; set; }

    public decimal OriginalPriceDiscountPercentage
    {
        get
        {
            if (OriginalPrice > 0 && Price < OriginalPrice)
            {
                //Keep one decimal place and do not round
                return Math.Truncate((1 - Price / OriginalPrice) * 100 * 10) / 10;
            }

            return 0;
        }
    }

    public decimal CutPriceDiscountPercentage { get; set; }
    public decimal DiscountPrice { get; set; }
    public decimal FollowBuyDiscountPercentageLabel { get; set; }

    public float Score { get; set; }

    public bool IsPresale { get; set; }

    public int UserOrderCountInThisMerch { get; set; }

    public string ExternalId { get; set; }

    public bool DisplaySampleLabel { get; set; }

    public bool IsDel { get; set; }

    public bool? CanSales { get; set; }
    public bool IsGroceryFood { get; set; }

    public bool HasFreeItem { get; set; }
}

public class ParameterGroupDto
{
    public ParameterGroupDto()
    {
        ParameterItems = new List<ParameterItemDto>();
    }

    public Guid Id { get; set; }

    public List<ParameterItemDto> ParameterItems { get; set; }

    public string MerchId { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public int Sort { get; set; }
    public int MinimumSelection { get; set; }

    private int? _maximumSelection;

    public int? MaximumSelection
    {
        get => _maximumSelection ?? ParameterItems.Count;
        set => _maximumSelection = value;
    }

    public bool ParticipateQuantityCalculation { get; set; }
}

public class ParameterItemDto
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    public string Name { get; set; }

    public decimal Price { get; set; }

    public int Sort { get; set; }
    public int SortV2 { get; set; }
    public int MinimumSelection { get; set; }

    public int? MaximumSelection { get; set; }

    public int SaleToCalculateUnitQtyConversion { get; set; }
}

public enum MerchFoodStatus
{
    OffSale,
    OnSale
}
