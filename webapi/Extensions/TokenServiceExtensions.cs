// Copyright (c) Microsoft. All rights reserved.

using CopilotChat.WebApi.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotChat.WebApi.Extensions;

internal static class TokenServiceExtensions
{
    public static IServiceCollection AddYesmealTokenService(this IServiceCollection services)
    {
        services.AddSingleton<ITokenManager, TokenManager>();
        return services;
    }
}
