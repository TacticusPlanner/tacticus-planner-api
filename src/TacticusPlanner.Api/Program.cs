using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using TacticusPlanner.Api.Features;
using TacticusPlanner.Api.Persistence;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.ServiceDefaults;
using TacticusPlanner.TacticusApi;

var builder = WebApplication.CreateBuilder(args);
var catalogOpenApiRoutes = new Dictionary<string, GameCatalogOpenApiRoute>(StringComparer.Ordinal)
{
    ["api/v1/game-catalog/manifest"] = new(
        "Gets the active game catalog manifest.",
        "Returns game catalog release metadata (version, schema version, game version), source hash, and the "
            + "denormalized dataset hashes + download URLs."
    ),
    ["api/v1/game-catalog/characters"] = new(
        "Gets game catalog characters.",
        "Returns all characters with faction/alliance, shard farm locations (drop chances inlined), and eligible equipment per slot."
    ),
    ["api/v1/game-catalog/npcs"] = new(
        "Gets game catalog NPCs.",
        "Returns all non-playable units."
    ),
    ["api/v1/game-catalog/mows"] = new(
        "Gets game catalog machines of war.",
        "Returns all machines of war."
    ),
    ["api/v1/game-catalog/mow-upgrade-costs"] = new(
        "Gets the machine-of-war upgrade-cost ladder.",
        "Returns the shared per-level upgrade-cost ladder used by all machines of war."
    ),
    ["api/v1/game-catalog/upgrades"] = new(
        "Gets game catalog upgrade materials.",
        "Returns all upgrades with farm locations (drop chances inlined) and, for craftable items, the recursively expanded recipe split into base and crafted totals."
    ),
    ["api/v1/game-catalog/equipment"] = new(
        "Gets game catalog equipment.",
        "Returns all equipment with the shared per-rarity upgrade-cost ladders inlined on the dataset."
    ),
    ["api/v1/game-catalog/campaign-battles"] = new(
        "Gets game catalog campaign battles.",
        "Returns all campaign battles (keyed by battle id, each carrying its campaignGroupId) with reward drop chances inlined on each potential reward."
    ),
    ["api/v1/game-catalog/campaign-definitions"] = new(
        "Gets game catalog campaign definitions.",
        "Returns all campaign groups (keyed by groupId) with metadata and the ids of the battles that belong to each group."
    ),
    ["api/v1/game-catalog/lres"] = new(
        "Gets game catalog legendary release events.",
        "Returns all legendary release events with per-track available unit ids resolved from the allowed-units filter."
    ),
};

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<PlannerDbContext>("planner-db");

builder.Services.AddProblemDetails();
builder.Services.AddGameCatalog();
builder.Services.AddTacticusApi(builder.Configuration["TacticusApi:BaseUrl"]);
builder.Services.AddFastEndpoints();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddOperationTransformer((operation, context, _) =>
    {
        if (context.Description.RelativePath is not null
            && catalogOpenApiRoutes.TryGetValue(context.Description.RelativePath, out var route))
        {
            operation.Summary = route.Summary;
            operation.Description = route.Description;
            operation.RequestBody = null;
        }

        return Task.CompletedTask;
    });

    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
        });

        return Task.CompletedTask;
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = "roles",
        };
    });

builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(
        AuthorizationPolicies.AccessAsUser,
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("sub");
            policy.RequireAssertion(context =>
                context.User
                    .FindAll("scp")
                    .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Contains("access_as_user", StringComparer.Ordinal)
            );
        }
    )
    .SetFallbackPolicy(
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build()
    );

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(options =>
{
    options.Endpoints.RoutePrefix = "api/v1";
    options.Endpoints.Configurator = endpoint =>
    {
        // GameCatalog endpoints are public (static game data) and opt out via AllowAnonymous in their own
        // Configure(); every other endpoint defaults to the authenticated AccessAsUser policy.
        if (endpoint.Routes?.Any(route => route.Contains("game-catalog", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return;
        }

        endpoint.Policies(AuthorizationPolicies.AccessAsUser);
    };
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
    app.MapScalarApiReference("/docs", options =>
    {
        options.WithTitle("Tacticus Planner API");
        options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
    }).AllowAnonymous();
}

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();

public partial class Program;

internal sealed record GameCatalogOpenApiRoute(
    string Summary,
    string Description
);
