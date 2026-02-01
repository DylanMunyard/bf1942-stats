using api.AI.Models;
using api.AI.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace api.AI;

/// <summary>
/// Extension methods for registering AI services.
/// </summary>
public static class AIServiceExtensions
{
    /// <summary>
    /// Adds AI services to the service collection.
    /// </summary>
    public static IServiceCollection AddAIServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        var aiSection = configuration.GetSection(AzureOpenAIOptions.SectionName);
        services.Configure<AzureOpenAIOptions>(aiSection);

        var options = aiSection.Get<AzureOpenAIOptions>();

        if (options == null || string.IsNullOrEmpty(options.Endpoint) || string.IsNullOrEmpty(options.ApiKey))
        {
            // AI services are optional - log warning but don't fail startup
            Console.WriteLine("Warning: Azure OpenAI configuration not found. AI features will be disabled.");
            return services;
        }

        if (string.IsNullOrEmpty(options.DeploymentName))
        {
            Console.WriteLine("Warning: AzureOpenAI:DeploymentName is not set. AI chat will fail with DeploymentNotFound until you set it to match your deployment name in Azure Portal (Model deployments).");
        }
        else
        {
            Console.WriteLine("Azure OpenAI: AI chat enabled, deployment name: {0}", options.DeploymentName);
        }

        // Register plugins as scoped services (they depend on scoped DbContext and services)
        services.AddScoped<PlayerStatsPlugin>();
        services.AddScoped<GameActivityPlugin>();
        services.AddScoped<ServerStatsPlugin>();

        // Register Semantic Kernel as scoped (plugins need scoped services)
        services.AddScoped<Kernel>(sp =>
        {
            var builder = Kernel.CreateBuilder();

            // Add Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: options.DeploymentName,
                endpoint: options.Endpoint,
                apiKey: options.ApiKey);

            var kernel = builder.Build();

            // Get scoped plugin instances and register them
            var playerStatsPlugin = sp.GetRequiredService<PlayerStatsPlugin>();
            var gameActivityPlugin = sp.GetRequiredService<GameActivityPlugin>();
            var serverStatsPlugin = sp.GetRequiredService<ServerStatsPlugin>();

            kernel.Plugins.AddFromObject(playerStatsPlugin, "PlayerStats");
            kernel.Plugins.AddFromObject(gameActivityPlugin, "GameActivity");
            kernel.Plugins.AddFromObject(serverStatsPlugin, "ServerStats");

            return kernel;
        });

        // Register AI service
        services.AddScoped<IAIService, AIService>();

        return services;
    }
}
