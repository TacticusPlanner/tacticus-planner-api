using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TacticusPlanner.AppHost;

internal static class LocalSetupHealthChecks
{
    public const string ApiHealthCheckName = "api-local-setup";
    public const string WebHealthCheckName = "web-local-setup";

    public static IDistributedApplicationBuilder AddLocalSetupHealthChecks(
        this IDistributedApplicationBuilder builder,
        string apiProjectPath,
        string webEnvPath
    )
    {
        var healthChecks = builder.Services.AddHealthChecks();

        healthChecks.AddCheck(
            ApiHealthCheckName,
            () => CheckApiSetup(builder.Configuration, apiProjectPath)
        );
        healthChecks.AddCheck(
            WebHealthCheckName,
            () => CheckWebSetup(webEnvPath)
        );

        return builder;
    }

    private static HealthCheckResult CheckApiSetup(
        IConfiguration configuration,
        string apiProjectPath
    )
    {
        var missing = new List<string>();
        var details = new Dictionary<string, object>
        {
            ["apiProjectPath"] = apiProjectPath,
        };
        var apiSecrets = ReadUserSecrets(apiProjectPath);

        RequireApiValue(
            configuration,
            apiSecrets,
            missing,
            "Authentication:Authority",
            "Authentication__Authority",
            "dotnet user-secrets set \"Authentication:Authority\" \"<ciam-authority>\" --project src/TacticusPlanner.Api"
        );
        RequireApiValue(
            configuration,
            apiSecrets,
            missing,
            "Authentication:Audience",
            "Authentication__Audience",
            "dotnet user-secrets set \"Authentication:Audience\" \"<local-api-application-client-id-guid>\" --project src/TacticusPlanner.Api"
        );

        AddApiSetupInstructions(details);

        if (missing.Count == 0)
        {
            return HealthCheckResult.Healthy("API local setup configuration is present.", details);
        }

        details["missing"] = string.Join(", ", missing);

        return HealthCheckResult.Degraded(
            $"API local setup is incomplete. Missing: {string.Join(", ", missing)}.",
            data: details
        );
    }

    private static HealthCheckResult CheckWebSetup(string webEnvPath)
    {
        var missing = new List<string>();
        var details = new Dictionary<string, object>
        {
            ["webEnvPath"] = webEnvPath,
        };
        var webEnv = ReadDotEnvFile(webEnvPath);
        foreach (
            var key in new[]
            {
                "VITE_API_SCOPE",
                "VITE_MSAL_CLIENT_ID",
                "VITE_MSAL_AUTHORITY",
                "VITE_MSAL_TENANT_ID",
            }
        )
        {
            if (!HasConfiguredValue(webEnv.GetValueOrDefault(key)))
            {
                missing.Add(key);
            }
        }

        AddWebSetupInstructions(details);

        if (missing.Count == 0)
        {
            return HealthCheckResult.Healthy("Web client local setup configuration is present.", details);
        }

        details["missing"] = string.Join(", ", missing);

        return HealthCheckResult.Degraded(
            $"Web client local setup is incomplete. Missing .env.local values: {string.Join(", ", missing)}.",
            data: details
        );
    }

    private static void AddApiSetupInstructions(Dictionary<string, object> details)
    {
        details["apiSecretCommands"] =
            "dotnet user-secrets set \"Authentication:Authority\" \"<ciam-authority>\" --project src/TacticusPlanner.Api"
            + Environment.NewLine
            + "dotnet user-secrets set \"Authentication:Audience\" \"<local-api-application-client-id-guid>\" --project src/TacticusPlanner.Api";
    }

    private static void AddWebSetupInstructions(Dictionary<string, object> details)
    {
        details["webEnvFile"] =
            "Copy apps/web/.env.example to apps/web/.env.local and fill in VITE_API_SCOPE, VITE_MSAL_CLIENT_ID, VITE_MSAL_AUTHORITY, and VITE_MSAL_TENANT_ID.";
    }

    private static void RequireApiValue(
        IConfiguration configuration,
        Dictionary<string, string?> apiSecrets,
        List<string> missing,
        string configKey,
        string environmentKey,
        string command
    )
    {
        var value =
            configuration[configKey]
            ?? Environment.GetEnvironmentVariable(environmentKey)
            ?? Environment.GetEnvironmentVariable(configKey)
            ?? apiSecrets.GetValueOrDefault(configKey);

        if (!HasConfiguredValue(value))
        {
            missing.Add($"API secret: {configKey} ({command})");
        }
    }

    private static Dictionary<string, string?> ReadDotEnvFile(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            values[key] = value;
        }

        return values;
    }

    private static Dictionary<string, string?> ReadUserSecrets(string projectPath)
    {
        var userSecretsId = ReadUserSecretsId(projectPath);
        if (string.IsNullOrWhiteSpace(userSecretsId))
        {
            return [];
        }

        var secretsPath = GetUserSecretsPath(userSecretsId);
        if (!File.Exists(secretsPath))
        {
            return [];
        }

        using var stream = File.OpenRead(secretsPath);
        using var document = JsonDocument.Parse(stream);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        FlattenJson(document.RootElement, null, values);

        return values;
    }

    private static string? ReadUserSecretsId(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return null;
        }

        var project = XDocument.Load(projectPath);
        return project
            .Descendants("UserSecretsId")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(HasValue);
    }

    private static string GetUserSecretsPath(string userSecretsId)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets",
                userSecretsId,
                "secrets.json"
            );
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".microsoft",
            "usersecrets",
            userSecretsId,
            "secrets.json"
        );
    }

    private static void FlattenJson(
        JsonElement element,
        string? prefix,
        Dictionary<string, string?> values
    )
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = HasValue(prefix) ? $"{prefix}:{property.Name}" : property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenJson(property.Value, key, values);
                continue;
            }

            values[key] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
        }
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasConfiguredValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Contains('<', StringComparison.Ordinal)
               && !value.Contains("your-tenant-subdomain", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(
                   value,
                   "00000000-0000-0000-0000-000000000000",
                   StringComparison.OrdinalIgnoreCase
               );
    }

}
