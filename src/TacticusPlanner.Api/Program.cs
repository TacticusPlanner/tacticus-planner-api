using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using TacticusPlanner.Api.Features;
using TacticusPlanner.Api.Persistence;
using TacticusPlanner.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<PlannerDbContext>("planner-db");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1", options =>
{
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
