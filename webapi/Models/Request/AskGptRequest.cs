// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using CopilotChat.WebApi.Dtos;

namespace CopilotChat.WebApi.Models.Request;

public class AskGptRequest
{
    public int Model { get; set; }
    public List<AskSmartiesMessageDto> Messages { get; set; }
}
