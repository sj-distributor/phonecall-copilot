// Copyright (c) Microsoft. All rights reserved.

using System;

namespace CopilotChat.WebApi.Models.Response;

public class AddOrderByMerchIdResponse
{
    public string OrderSerialNumber { get; set; }
    public bool IsAddSuccessful { get; set; }
    public DateTimeOffset? PickupTime { get; set; }
    public string MealCode { get; set; }
}
