// Copyright (c) Microsoft. All rights reserved.

using System;

namespace CopilotChat.WebApi.Models.Request;

public class EmptyShoppingCartRequest
{
    public EmptyShoppingCartRequest()
    {
        RunInBackground = true;
    }

    public Guid MerchId { get; set; }

    public bool RunInBackground { get; set; }

    public bool ShouldThrowGroupifyError { get; set; } = true;
    public bool ShouldExcludePickupOrFallbackMerchants { get; set; }
}
