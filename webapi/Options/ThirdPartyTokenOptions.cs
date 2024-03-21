// Copyright (c) Microsoft. All rights reserved.

namespace CopilotChat.WebApi.Options;

public class ThirdPartyTokenOptions
{
    public const string PropertyName = "ThirdPartyToken";

    /// <summary>
    /// Local directory from which to load native plugins.
    /// </summary>
    public string Yesmeal { get; set; }

    /// <summary>
    /// Setting indicating if the site is undergoing maintenance.
    /// </summary>
    public string Smarties { get; set; }
}
