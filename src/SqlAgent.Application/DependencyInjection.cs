using Microsoft.Extensions.DependencyInjection;
using SqlAgent.Application.Services;
using SqlAgent.Domain.Contracts;

namespace SqlAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<ISqlSafetyValidator, SqlSafetyValidator>();
        services.AddScoped<IQueryAgentService, QueryAgentService>();
        return services;
    }
}
