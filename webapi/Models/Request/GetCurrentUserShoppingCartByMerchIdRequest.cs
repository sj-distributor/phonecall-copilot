// Copyright (c) Microsoft. All rights reserved.

using System;
using CopilotChat.WebApi.Dtos;

namespace CopilotChat.WebApi.Models.Request;

public class GetCurrentUserShoppingCartByMerchIdRequest
{
    public Guid MerchId { get; set; }

    public DeliveryType? DeliveryType { get; set; }

    public bool ShouldThrowGroupifyError { get; set; } = true;
    public bool ShouldExcludePickupOrFallbackMerchants { get; set; }
}
