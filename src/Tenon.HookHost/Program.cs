using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Tenon.Hooks;

namespace Tenon.HookHost;

/// <summary>
/// tenon-hookhost.exe --request &lt;file&gt; [--stdout]
/// Loads the developer's hooks assembly, runs the methods of the requested stage and reports
/// events as JSON lines on stdout. Exit codes: 0 ok, 1602 cancelled, 1603 failed.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string? requestPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--request" && i + 1 < args.Length) requestPath = args[++i];
        }

        HookRequest request;
        try
        {
            var json = requestPath != null ? File.ReadAllText(requestPath) : Console.In.ReadToEnd();
            request = HookRequest.FromJson(json);
        }
        catch (Exception ex)
        {
            Emit(new HookEvent { Type = "error", Message = "Invalid hook request: " + ex.Message });
            return 1603;
        }

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var result = new HookRunner(request, Emit, cts.Token).Run();
            Emit(new HookEvent { Type = "done", ExitCode = result });
            return result;
        }
        catch (Exception ex)
        {
            Emit(new HookEvent { Type = "error", Message = ex.Message });
            Emit(new HookEvent { Type = "done", ExitCode = 1603 });
            return 1603;
        }
    }

    private static readonly object Gate = new();

    private static void Emit(HookEvent e)
    {
        lock (Gate)
        {
            Console.Out.WriteLine(e.ToJson());
            Console.Out.Flush();
        }
    }
}

internal sealed class HookRunner
{
    private readonly HookRequest _request;
    private readonly Action<HookEvent> _emit;
    private readonly CancellationToken _ct;

    public HookRunner(HookRequest request, Action<HookEvent> emit, CancellationToken ct)
    {
        _request = request;
        _emit = emit;
        _ct = ct;
    }

    public int Run()
    {
        if (!Enum.TryParse<HookStage>(_request.Stage, ignoreCase: true, out var stage))
            throw new InvalidOperationException($"Unknown hook stage '{_request.Stage}'.");

        var assemblyPath = Path.Combine(_request.HooksDirectory, _request.AssemblyName);
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException($"Hooks assembly not found: {assemblyPath}");

        var context = new HostContext(_request, stage, _emit, _ct);
        var alc = new HooksLoadContext(assemblyPath);
        var assembly = alc.LoadFromAssemblyPath(assemblyPath);

        var methods = new List<(int order, object? instance, MethodInfo method, SetupHookAttribute attr)>();
        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ISetupHooks).IsAssignableFrom(t)))
        {
            object? instance = null;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var attr in m.GetCustomAttributes<SetupHookAttribute>())
                {
                    if (attr.Stage != stage) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1 || !typeof(SetupContext).IsAssignableFrom(ps[0].ParameterType))
                        throw new InvalidOperationException($"{type.FullName}.{m.Name} must take a single SetupContext parameter.");
                    if (!m.IsStatic) instance ??= Activator.CreateInstance(type);
                    methods.Add((attr.Order, m.IsStatic ? null : instance, m, attr));
                }
            }
        }

        context.Log($"Running {methods.Count} {stage} hook(s) from {assembly.GetName().Name}");
        foreach (var (_, instance, method, attr) in methods.OrderBy(x => x.order))
        {
            _ct.ThrowIfCancellationRequested();
            try
            {
                context.Log($"Hook {method.DeclaringType!.Name}.{method.Name}");
                var result = method.Invoke(instance, new object[] { context });
                if (result is Task task) task.GetAwaiter().GetResult();
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                var inner = tie.InnerException;
                if (inner is OperationCanceledException) return 1602;
                if (attr.ContinueOnError)
                {
                    context.LogWarning($"{method.Name} failed and was ignored: {inner.Message}");
                    continue;
                }
                var message = inner is SetupHookException ? inner.Message : $"{method.Name} failed: {inner}";
                _emit(new HookEvent { Type = "error", Message = message });
                return 1603;
            }
        }
        return 0;
    }
}

internal sealed class HooksLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public HooksLoadContext(string mainAssembly) : base("TenonHooks", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(mainAssembly);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // Share the SDK types with the host so ISetupHooks/SetupHookAttribute are the same identities.
        if (name.Name == "Tenon.Hooks") return null;
        var path = _resolver.ResolveAssemblyToPath(name);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

internal sealed class HostContext : SetupContext
{
    private readonly HookRequest _r;
    private readonly Action<HookEvent> _emit;

    public HostContext(HookRequest r, HookStage stage, Action<HookEvent> emit, CancellationToken ct)
    {
        _r = r;
        Stage = stage;
        _emit = emit;
        Cancellation = ct;
        Version = System.Version.TryParse(r.Version, out var v) ? v : new Version(0, 0);
        PreviousVersion = r.PreviousVersion != null && System.Version.TryParse(r.PreviousVersion, out var pv) ? pv : null!;
        if (!string.IsNullOrEmpty(r.DataDirectory)) Directory.CreateDirectory(r.DataDirectory);
    }

    public override HookStage Stage { get; }
    public override string ProductName => _r.ProductName;
    public override Version Version { get; }
    public override string InstallDir => _r.InstallDir;
    public override bool IsPerMachine => _r.PerMachine;
    public override bool IsUpgrade => _r.Upgrade;
    public override Version PreviousVersion { get; }
    public override bool IsUninstall => _r.Uninstall;
    public override bool IsRepair => _r.Repair;
    public override bool IsElevated => _r.Elevated;
    public override UiLevel UiLevel => (UiLevel)_r.UiLevel;
    public override IReadOnlyDictionary<string, string> Properties => _r.Properties;
    public override CancellationToken Cancellation { get; }
    public override string DataDirectory => _r.DataDirectory;
    public override bool Task(string id) => _r.Tasks.Contains(id, StringComparer.OrdinalIgnoreCase);

    public override void SetProperty(string name, string value)
    {
        if (Stage != HookStage.Prepare) throw new InvalidOperationException("Properties can only be set in the Prepare stage.");
        _r.Properties[name] = value;
        _emit(new HookEvent { Type = "setProperty", Name = name, Value = value });
    }

    public override void Log(string message) => _emit(new HookEvent { Type = "log", Message = message });
    public override void LogWarning(string message) => _emit(new HookEvent { Type = "warning", Message = message });
    public override void Progress(string status) => _emit(new HookEvent { Type = "progress", Message = status });
    public override void RequireReboot() => _emit(new HookEvent { Type = "reboot" });
}
