using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using TacticusPlanner.Api.Features;
using TacticusPlanner.Api.Persistence;
using TacticusPlanner.Catalog;
using TacticusPlanner.ServiceDefaults;
using TacticusPlanner.TacticusApi;

var builder = WebApplication.CreateBuilder(args);
var catalogOpenApiRoutes = new Dictionary<string, CatalogOpenApiRoute>(StringComparer.Ordinal)
{
    ["api/v1/catalog/manifest"] = new(
        "Gets the active catalog manifest.",
        "Returns catalog release metadata, source hash, and per-dataset hashes.",
        []
    ),
    ["api/v1/catalog/units"] = new(
        "Gets catalog units.",
        "Returns active catalog units with optional filters.",
        ["search", "unitKind", "faction", "alliance"]
    ),
    ["api/v1/catalog/mows"] = new(
        "Gets catalog machines of war.",
        "Returns active catalog machines of war with optional filters.",
        ["search", "faction", "alliance"]
    ),
    ["api/v1/catalog/upgrades"] = new(
        "Gets catalog upgrade materials.",
        "Returns active catalog upgrade materials with optional filters.",
        ["search", "rarity"]
    ),
    ["api/v1/catalog/equipment"] = new(
        "Gets catalog equipment.",
        "Returns active catalog equipment with optional filters.",
        ["search", "rarity", "type"]
    ),
    ["api/v1/catalog/campaigns"] = new(
        "Gets catalog campaigns.",
        "Returns active catalog campaigns with optional filters.",
        ["search", "releaseType", "groupType", "difficulty"]
    ),
    ["api/v1/catalog/campaign-events"] = new(
        "Gets catalog campaign events.",
        "Returns active catalog campaign events with optional filters.",
        ["search", "groupType", "difficulty"]
    ),
    ["api/v1/catalog/campaign-battles"] = new(
        "Gets catalog campaign battles.",
        "Returns active catalog campaign battles with optional filters.",
        ["search", "campaignId", "campaignType", "rewardId"]
    ),
    ["api/v1/catalog/lres"] = new(
        "Gets catalog legendary release events.",
        "Returns active catalog legendary release events with optional filters.",
        ["search", "unitId", "finished"]
    ),
};

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<PlannerDbContext>("planner-db");

builder.Services.AddProblemDetails();
builder.Services.AddCatalog();
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
            operation.Parameters ??= [];

            foreach (var parameter in route.QueryParameters)
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = parameter,
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = $"Optional {parameter} filter.",
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                    },
                });
            }
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

internal sealed record CatalogOpenApiRoute(
    string Summary,
    string Description,
    IReadOnlyList<string> QueryParameters
);
