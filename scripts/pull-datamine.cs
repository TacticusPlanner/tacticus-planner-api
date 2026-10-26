#:property RestorePackagesWithLockFile=false

// Copies the allowlisted raw files of TacticusPlanner/tacticus-planner-data-mines into the game catalog
// and pins the commit they came from. See openspec/specs/game-catalog-datamine-source.
//
//   dotnet run scripts/pull-datamine.cs -- v1.43.157.2989        pin a release tag (or a commit SHA)
//   dotnet run scripts/pull-datamine.cs -- --source ../tacticus-planner-data-mines
//                                                                 copy from a working tree (writes a `local`
//                                                                 pin, which the tests reject)
//
// Remote mode uses `git` with the caller's own credentials (the data-mines repo is private).

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

const string DefaultRemote = "https://github.com/TacticusPlanner/tacticus-planner-data-mines.git";

string? source = null;
string? reference = null;
var remote = DefaultRemote;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--source" when i + 1 < args.Length:
            source = args[++i];
            break;
        case "--remote" when i + 1 < args.Length:
            remote = args[++i];
            break;
        default:
            reference = args[i];
            break;
    }
}

if ((source is null) == (reference is null))
{
    Console.Error.WriteLine("usage: dotnet run scripts/pull-datamine.cs -- <tag-or-sha> | --source <data-mines checkout> [--remote <url>]");
    return 2;
}

var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var datamineDir = Path.Combine(repoRoot, "src", "TacticusPlanner.GameCatalog", "Data", "datamine");
var rawDir = Path.Combine(datamineDir, "raw");
var paths = ReadAllowlist(Path.Combine(datamineDir, "files.txt"));

string pin;
string version;
var contents = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
string? tempDir = null;
try
{
    if (source is not null)
    {
        var sourceRaw = Path.Combine(source, "raw");
        var missing = paths.Where(path => !File.Exists(Path.Combine(sourceRaw, path))).ToList();
        if (missing.Count > 0)
        {
            return Fail(missing, source);
        }

        foreach (var path in paths)
        {
            contents[path] = File.ReadAllBytes(Path.Combine(sourceRaw, path));
        }

        version = File.ReadAllText(Path.Combine(sourceRaw, "VERSION")).Trim();
        pin = $"local {source.Replace('\\', '/')}";
    }
    else
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"pull-datamine-{Guid.NewGuid():N}");
        Git(null, "clone", "--quiet", "--filter=blob:none", "--no-checkout", remote, tempDir);
        var sha = Encoding.UTF8.GetString(Git(tempDir, "rev-parse", "--verify", $"{reference}^{{commit}}")).Trim();
        var tags = Encoding.UTF8.GetString(Git(tempDir, "tag", "--points-at", sha))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.StartsWith('v'))
            .Order(StringComparer.Ordinal)
            .ToList();
        var tag = tags.Contains(reference!) ? reference! : tags.LastOrDefault() ?? "untagged";

        var missing = paths.Where(path => !TryGit(tempDir, "cat-file", "-e", $"{sha}:raw/{path}")).ToList();
        if (missing.Count > 0)
        {
            return Fail(missing, $"{remote} at {sha}");
        }

        foreach (var path in paths)
        {
            contents[path] = Git(tempDir, "show", $"{sha}:raw/{path}");
        }

        version = Encoding.UTF8.GetString(Git(tempDir, "show", $"{sha}:raw/VERSION")).Trim();
        pin = $"{tag} {sha}";
    }
}
finally
{
    if (tempDir is not null && Directory.Exists(tempDir))
    {
        DeleteDirectory(tempDir);
    }
}

Console.WriteLine($"datamine {version} ({pin})");

var existing = Directory.Exists(rawDir)
    ? Directory.EnumerateFiles(rawDir, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(rawDir, file).Replace('\\', '/'))
        .ToHashSet(StringComparer.Ordinal)
    : [];

foreach (var (path, bytes) in contents)
{
    var target = Path.Combine(rawDir, path);
    var status = !existing.Contains(path) ? "added"
        : File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes) ? "unchanged"
        : "changed";
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, bytes);
    Console.WriteLine($"  {status,-9} {path}");
}

foreach (var path in existing.Where(path => !contents.ContainsKey(path)).Order(StringComparer.Ordinal))
{
    File.Delete(Path.Combine(rawDir, path));
    Console.WriteLine($"  {"removed",-9} {path}");
}

if (Directory.Exists(rawDir))
{
    foreach (var dir in Directory.EnumerateDirectories(rawDir, "*", SearchOption.AllDirectories).OrderByDescending(dir => dir.Length))
    {
        if (!Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
        }
    }
}

var checksums = new StringBuilder();
foreach (var (path, bytes) in contents)
{
    checksums.Append(Convert.ToHexStringLower(SHA256.HashData(bytes))).Append("  ").Append(path).Append('\n');
}

File.WriteAllText(Path.Combine(datamineDir, "files.sha256"), checksums.ToString());
File.WriteAllText(Path.Combine(datamineDir, "REF"), pin + "\n");
return 0;

static int Fail(IEnumerable<string> missing, string where)
{
    foreach (var path in missing)
    {
        Console.Error.WriteLine($"raw/{path} is listed in files.txt but does not exist in {where}");
    }

    Console.Error.WriteLine("Nothing was changed.");
    return 1;
}

static string FindRepoRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "TacticusPlanner.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run this from inside the tacticus-planner-api repository.");
}

static List<string> ReadAllowlist(string file) =>
    File.ReadAllLines(file)
        .Select(line => (line.IndexOf('#') is var hash and >= 0 ? line[..hash] : line).Trim())
        .Where(line => line.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

static byte[] Git(string? workingDirectory, params string[] arguments)
{
    var (exitCode, stdout, stderr) = RunGit(workingDirectory, arguments);
    if (exitCode != 0)
    {
        throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr.Trim()}");
    }

    return stdout;
}

static bool TryGit(string workingDirectory, params string[] arguments) => RunGit(workingDirectory, arguments).ExitCode == 0;

static (int ExitCode, byte[] Stdout, string Stderr) RunGit(string? workingDirectory, string[] arguments)
{
    var startInfo = new ProcessStartInfo("git")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
    };

    // Run from a git hook, these would point git at this repository instead of the temp clone.
    foreach (var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_COMMON_DIR", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_PREFIX" })
    {
        startInfo.Environment.Remove(variable);
    }
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)!;
    var stderr = process.StandardError.ReadToEndAsync();
    using var stdout = new MemoryStream();
    process.StandardOutput.BaseStream.CopyTo(stdout);
    process.WaitForExit();
    return (process.ExitCode, stdout.ToArray(), stderr.Result);
}

static void DeleteDirectory(string path)
{
    // git marks pack files read-only, which Directory.Delete refuses on Windows.
    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(file, FileAttributes.Normal);
    }

    Directory.Delete(path, recursive: true);
}
