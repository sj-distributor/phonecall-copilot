// Copyright (c) Microsoft. All rights reserved.

using System;

namespace CopilotChat.WebApi.Models.Request;

public class AddOrderByMerchIdRequest
{
    public Guid MerchId { get; set; }
}
