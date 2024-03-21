// Copyright (c) Microsoft. All rights reserved.

using CopilotChat.WebApi.Dtos;

namespace CopilotChat.WebApi.Models.Response;

public class AskGptResponse
{
    public CompletionsResponseDto Data { get; set; }
}
