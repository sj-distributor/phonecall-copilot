// Copyright (c) Microsoft. All rights reserved.

using System;

namespace CopilotChat.WebApi.Dtos;

public class FoodParameterDto
{
    public Guid ParameterId { get; set; }
    public int Quantity { get; set; }
    public Guid ParameterGroupId { get; set; }
}

public class FoodParameterMapDto
{
    public Guid FoodId { get; set; }
    public Guid ParameterItemId { get; set; }
}
