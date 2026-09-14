namespace Tenon.Core.Logging;

public interface ITenonLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleLogger : ITenonLogger
{
    public bool Verbose { get; set; }

    public void Debug(string message)
    {
        if (Verbose) Console.WriteLine("  " + message);
    }

    public void Info(string message) => Console.WriteLine(message);

    public void Warn(string message)
    {
        var c = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("warning: " + message);
        Console.ForegroundColor = c;
    }

    public void Error(string message)
    {
        var c = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("error: " + message);
        Console.ForegroundColor = c;
    }
}

public sealed class NullLogger : ITenonLogger
{
    public static readonly NullLogger Instance = new();
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}
