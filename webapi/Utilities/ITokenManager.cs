// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CopilotChat.WebApi.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CopilotChat.WebApi.Utilities;

public interface ITokenManager
{
    string GetToken(string chatId);

    string SetToken(string chatId);

    void ReleaseToken(string chatId);
}

public class TokenManager : ITokenManager
{
    private ConcurrentDictionary<string, string> userTokenMap = new ConcurrentDictionary<string, string>();
    private HashSet<string> usedTokens = new HashSet<string>();
    private ConcurrentQueue<string> availableTokens;

    public TokenManager(IServiceProvider sp)
    {
        var thirdPartyTokens = sp.GetRequiredService<IOptions<ThirdPartyTokenOptions>>();
        availableTokens = new ConcurrentQueue<string>(thirdPartyTokens.Value.YesmealTokensForClient.Split(","));
    }

    public string GetToken(string chatId)
    {
        userTokenMap.TryGetValue(chatId, out string token);
        return token;
    }

    public string SetToken(string chatId)
    {
        string token = string.Empty;
        if (availableTokens.TryDequeue(out token))
        {
            userTokenMap.TryAdd(chatId, token);
            usedTokens.Add(token);
        }

        return token;
    }

    public void ReleaseToken(string chatId)
    {
        if (userTokenMap.TryRemove(chatId, out string token))
        {
            usedTokens.Remove(token);
            availableTokens.Enqueue(token);
        }
    }
}
