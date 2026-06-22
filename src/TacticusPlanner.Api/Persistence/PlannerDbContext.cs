using Microsoft.EntityFrameworkCore;

namespace TacticusPlanner.Api.Persistence;

public sealed class PlannerDbContext(DbContextOptions<PlannerDbContext> options)
    : DbContext(options);
