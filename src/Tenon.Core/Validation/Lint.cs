namespace Tenon.Core.Validation;

public enum LintSeverity { Info, Warning, Error }

/// <summary>A single validation finding with a stable id (TNxxxx), a human message and a suggested fix.</summary>
public sealed record LintMessage(string Id, LintSeverity Severity, string Message, string? Fix = null, string? Location = null)
{
    public override string ToString()
    {
        var s = $"{Id} {Severity.ToString().ToLowerInvariant()}: {Message}";
        if (Location != null) s += $" (at {Location})";
        if (Fix != null) s += $"\n    fix: {Fix}";
        return s;
    }
}

/// <summary>Collects lint messages during planning.</summary>
public sealed class LintCollector
{
    private readonly List<LintMessage> _messages = new();

    public IReadOnlyList<LintMessage> Messages => _messages;
    public bool HasErrors => _messages.Any(m => m.Severity == LintSeverity.Error);

    public void Add(LintMessage message) => _messages.Add(message);

    public void Error(string id, string message, string? fix = null, string? location = null)
        => _messages.Add(new LintMessage(id, LintSeverity.Error, message, fix, location));

    public void Warning(string id, string message, string? fix = null, string? location = null)
        => _messages.Add(new LintMessage(id, LintSeverity.Warning, message, fix, location));

    public void Info(string id, string message, string? fix = null, string? location = null)
        => _messages.Add(new LintMessage(id, LintSeverity.Info, message, fix, location));

    public void ThrowIfErrors()
    {
        if (HasErrors) throw new LintException(_messages.Where(m => m.Severity == LintSeverity.Error).ToList());
    }
}

public sealed class LintException : Exception
{
    public IReadOnlyList<LintMessage> Errors { get; }

    public LintException(IReadOnlyList<LintMessage> errors)
        : base(errors.Count == 1 ? errors[0].ToString() : $"{errors.Count} errors:\n" + string.Join("\n", errors))
    {
        Errors = errors;
    }
}

/// <summary>Well-known rule ids so messages stay consistent across the code base.</summary>
public static class LintIds
{
    public const string MissingUpgradeCode = "TN0001";
    public const string MissingProductName = "TN0002";
    public const string MissingPublisher = "TN0003";
    public const string InvalidVersion = "TN0004";
    public const string DuplicateTargetFile = "TN0010";
    public const string GlobMatchedNothing = "TN0011";
    public const string SourceFileMissing = "TN0012";
    public const string InvalidIdentifier = "TN0020";
    public const string VersionFourthPart = "TN0030";
    public const string UnknownPathConstant = "TN0040";
    public const string InvalidInstallDir = "TN0041";
    public const string ScopeMix = "TN0057";
    public const string RequiresPerMachine = "TN0060";
    public const string ShortcutTargetMissing = "TN0070";
    public const string TooManyFiles = "TN0080";
    public const string CodepageMismatch = "TN0090";
    public const string MainExeMissing = "TN0100";
    public const string UnknownTask = "TN0110";
    public const string UnknownMacro = "TN0120";
    public const string HookRuntime = "TN0200";
}
