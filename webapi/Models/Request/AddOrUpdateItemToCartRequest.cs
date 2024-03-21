// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using CopilotChat.WebApi.Dtos;

namespace CopilotChat.WebApi.Models.Request;

public class AddOrUpdateItemToCartRequest
{
    public AddOrUpdateItemToCartRequest()
    {
        FoodParameters = new List<FoodParameterDto>();
    }

    public Guid MerchId { get; set; }

    public Guid FoodId { get; set; }

    private int _quantity;

    public int Quantity
    {
        get => TotalNumberOfSameSpecifications ?? _quantity;
        set => _quantity = value;
    }
    public IEnumerable<FoodParameterDto> FoodParameters { get; set; }

    public Guid? ShoppingCartItemId { get; set; }

    public DeliveryType? DeliveryType { get; set; }

    public bool ShouldThrowGroupifyError { get; set; } = true;
    public bool ShouldExcludePickupOrFallbackMerchants { get; set; }

    public int? TotalNumberOfSameSpecifications { get; set; }
}
