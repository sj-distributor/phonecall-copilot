// Copyright (c) Microsoft. All rights reserved.

using CopilotChat.WebApi.Dtos;

namespace CopilotChat.WebApi.Models.Response;

public class GetCurrentUserShoppingCartByMerchIdResponse
{
    public ShoppingCartDto ShoppingCart { get; set; }
}
