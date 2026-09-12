using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Auth;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        // PlannerDbContext is pooled (AddNpgsqlDbContext) and so cannot depend on a scoped service; this
        // singleton resolves the current request's profile via IHttpContextAccessor instead — see
        // ICurrentProfileProvider and PlannerDbContext.ApplyProfileQueryFilters for the global tenant-isolation
        // filters this feeds.
        services.AddSingleton<ICurrentProfileProvider, HttpContextCurrentProfileProvider>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Authority"];
                options.Audience = configuration["Authentication:Audience"];
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "roles",
                };
            });

        services
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

        return services;
    }
}
