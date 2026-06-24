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
        "Returns catalog release metadata, source hash, per-dataset hashes, and dataset download URLs."
    ),
    ["api/v1/catalog/units"] = new(
        "Gets catalog units.",
        "Returns the complete active catalog units chunk."
    ),
    ["api/v1/catalog/mows"] = new(
        "Gets catalog machines of war.",
        "Returns the complete active catalog machines of war chunk."
    ),
    ["api/v1/catalog/upgrades"] = new(
        "Gets catalog upgrade materials.",
        "Returns the complete active catalog upgrade materials chunk."
    ),
    ["api/v1/catalog/equipment"] = new(
        "Gets catalog equipment.",
        "Returns the complete active catalog equipment chunk."
    ),
    ["api/v1/catalog/campaigns"] = new(
        "Gets catalog campaigns.",
        "Returns the complete active regular catalog campaigns chunk."
    ),
    ["api/v1/catalog/campaign-events"] = new(
        "Gets catalog campaign events.",
        "Returns the complete active catalog campaign events chunk."
    ),
    ["api/v1/catalog/campaign-battles"] = new(
        "Gets catalog campaign battles.",
        "Returns the complete active catalog campaign battles chunk."
    ),
    ["api/v1/catalog/lres"] = new(
        "Gets catalog legendary release events.",
        "Returns the complete active catalog legendary release events chunk."
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
    string Description
);
