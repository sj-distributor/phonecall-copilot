// Copyright (c) Microsoft. All rights reserved.

namespace CopilotChat.WebApi.Dtos;

public class IntentValueDto
{
    public string Intent { get; set; }
    public int Value { get; set; }
    public string FoodName { get; set; }
}

public class PhraseIntentValueDto
{
    public IntentValueDto IntentValue { get; set; }
    public bool IsPositive { get; set; }
}
