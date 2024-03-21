// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace CopilotChat.WebApi.Models.Request;

public class RecommendSimilarFoodsRequest
{
    public string Keyword { get; set; }

    public Guid? FoodId { get; set; }

    public List<Guid> MerchIds { get; set; }

    public List<Guid> FoodsPool { get; set; }

    public List<Guid> ExcludeFoodIds { get; set; }

    public int RecommendCount { get; set; } = 1;

    public bool UseDefaultFallback { get; set; } = true;
}
