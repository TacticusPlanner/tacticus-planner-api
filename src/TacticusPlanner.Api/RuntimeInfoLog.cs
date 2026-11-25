using System.Runtime;

namespace TacticusPlanner.Api;

/// <summary>
/// Logs once at startup what the runtime thinks it has (processors, GC mode, the GC's memory limit), so memory
/// growth in prod can be read against the GC's own limits rather than guessed.
/// </summary>
public static partial class RuntimeInfoLog
{
    private const long Mebibyte = 1024 * 1024;

    public static void Write(ILogger logger)
    {
        var gcMemory = GC.GetGCMemoryInfo();
        RuntimeInfo(
            logger,
            Environment.ProcessorCount,
            GCSettings.IsServerGC,
            gcMemory.TotalAvailableMemoryBytes / Mebibyte,
            gcMemory.HighMemoryLoadThresholdBytes / Mebibyte
        );
    }

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Information,
        Message = "Runtime: {ProcessorCount} processors, server GC {IsServerGc}, GC available memory {AvailableMemoryMb} MB, high memory load threshold {HighMemoryLoadMb} MB."
    )]
    private static partial void RuntimeInfo(
        ILogger logger,
        int processorCount,
        bool isServerGc,
        long availableMemoryMb,
        long highMemoryLoadMb
    );
}
