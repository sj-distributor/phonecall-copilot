// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using CopilotChat.WebApi.Dtos;
using Newtonsoft.Json;

namespace CopilotChat.WebApi.Models.Request;

public class AskGptRequest
{
    public int Model { get; set; }
    public List<AskSmartiesMessageDto> Messages { get; set; }
    [JsonProperty("response_format")]
    public ResponseFormat ResponseFormat { get; set; }
}

public class ResponseFormat
{
    public string Type { get; set; }
}
