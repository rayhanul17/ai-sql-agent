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

        // AI providers (Semantic Kernel). Model is chosen per request; the
        // resolver picks Ollama (local) or Groq (cloud) per request.
        services.AddSingleton<IAiProvider>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaAiProvider(new Uri(opts.BaseUrl));
        });
        services.AddSingleton<IAiProvider>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<GroqOptions>>().Value;
            return new GroqAiProvider(opts.ApiKey, opts.BaseUrl);
        });
        services.AddSingleton<IAiProviderResolver, AiProviderResolver>();

        // Model manager uses a typed HttpClient to Ollama's management API.
        // A long timeout is required: warming up a large model is a COLD load
        // into RAM and can take minutes on CPU-only machines.
        services.AddHttpClient<IModelManager, OllamaModelManager>(c =>
            c.Timeout = TimeSpan.FromMinutes(10));

        return services;
    }
}
