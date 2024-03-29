// Copyright (c) Microsoft. All rights reserved.

namespace CopilotChat.WebApi.Dtos;

public class AskIntentDto
{
    public bool IsAsking { get; set; }
    public string ActionContent { get; set; }

    public string AnswerPhrase { get; set; }
}
