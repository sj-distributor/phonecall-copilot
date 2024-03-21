// Copyright (c) Microsoft. All rights reserved.

namespace CopilotChat.WebApi.Models.Request;

public class TranslationRequest
{
    public string Content { get; set; }

    public int TargetLanguage { get; set; }

    public int TranslateFrom { get; set; }
}
