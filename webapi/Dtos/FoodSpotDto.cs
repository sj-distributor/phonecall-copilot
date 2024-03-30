// Copyright (c) Microsoft. All rights reserved.

namespace CopilotChat.WebApi.Dtos;

public class FoodSpotDto
{
    public string FoodName { get; set; }

    public int? Quantity { get; set; }

    public string SpecialRequirement { get; set; }
}
