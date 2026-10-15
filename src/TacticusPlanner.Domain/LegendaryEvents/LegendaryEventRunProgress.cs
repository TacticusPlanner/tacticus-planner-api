namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>The manual economy inputs of one run (1–3) of a <see cref="LegendaryEventPlan"/> (LRE Stage 3): what
/// the Tacticus API cannot see. A run with no row reads as all zeros.</summary>
public class LegendaryEventRunProgress
{
    public LegendaryEventPlanId PlanId { get; set; }

    public int Run { get; set; }

    /// <summary>Regular missions completed, 0..the event's catalog <c>regularMissions</c> count.</summary>
    public int RegularMissions { get; set; }

    /// <summary>Premium missions completed, 0..the event's catalog <c>premiumMissions</c> count.</summary>
    public int PremiumMissions { get; set; }

    /// <summary>Whether the 300-currency bundle was bought for this run.</summary>
    public bool BundlePurchased { get; set; }

    /// <summary>"Oh So Close" shards awarded in this run, 0..<see cref="LegendaryEventValidation.MaxCloseShards"/>.</summary>
    public int CloseShards { get; set; }

    /// <summary>When this run's inputs were last written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public virtual LegendaryEventPlan? Plan { get; set; }
}
