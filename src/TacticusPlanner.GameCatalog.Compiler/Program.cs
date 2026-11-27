using TacticusPlanner.GameCatalog;

// Compiles the embedded raw catalog and writes the compiled catalog the runtime embeds. Run by the
// TacticusPlanner.GameCatalog build; fails (non-zero exit, errors on stderr) when the raw data does not validate.
if (args.Length != 1)
{
    await Console.Error.WriteLineAsync("usage: TacticusPlanner.GameCatalog.Compiler <output directory>");
    return 2;
}

try
{
    // The calendar is not compiled (the runtime projects it per day), so the instant only matters to validation.
    GameCatalogCompiledWriter.Write(GameCatalogCompiler.Compile(DateTimeOffset.UtcNow), args[0]);
    return 0;
}
catch (InvalidOperationException exception)
{
    await Console.Error.WriteLineAsync($"error GC0001: {exception.Message}");
    return 1;
}
