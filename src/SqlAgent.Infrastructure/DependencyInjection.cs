using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlAgent.Application.Options;
using SqlAgent.Domain.Contracts;
using SqlAgent.Infrastructure.Ai;
using SqlAgent.Infrastructure.Database;
using SqlAgent.Infrastructure.Dialects;

namespace SqlAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Dialects (Postgres/MySQL/SqlServer) + factory.
        services.AddSingleton<ISqlDialect, PostgreSqlDialect>();
        services.AddSingleton<ISqlDialect, MySqlDialect>();
        services.AddSingleton<ISqlDialect, SqlServerDialect>();
        services.AddSingleton<ISqlDialectFactory, SqlDialectFactory>();

        // Database access.
        services.AddSingleton<DbConnectionFactory>();
        services.AddScoped<ISchemaIntrospector, SchemaIntrospector>();
        services.AddScoped<ISqlExecutor, SqlExecutor>();

        // AI provider (Semantic Kernel + Ollama). Model is chosen per request.
        services.AddSingleton<IAiProvider>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaAiProvider(new Uri(opts.BaseUrl));
        });

        // Model manager uses a typed HttpClient to Ollama's management API.
        services.AddHttpClient<IModelManager, OllamaModelManager>();

        return services;
    }
}
