using System;
using System.Collections.Generic;
using System.Threading;

namespace Tenon.Hooks
{
    /// <summary>When a hook runs. Deferred stages run with the privileges of the installation and cannot change properties.</summary>
    public enum HookStage
    {
        /// <summary>Before anything changes; may set properties and inspect the machine. Runs as the user.</summary>
        Prepare,
        /// <summary>After the previous version was removed (on upgrade) and before files are copied.</summary>
        BeforeInstall,
        /// <summary>After files, shortcuts and registry values are written. Rolled back automatically on failure.</summary>
        AfterInstall,
        /// <summary>Before files are removed during uninstall (not during an upgrade).</summary>
        BeforeUninstall,
        /// <summary>After the product was removed during uninstall.</summary>
        AfterUninstall,
        /// <summary>Called when an installation that ran BeforeInstall/AfterInstall hooks is rolled back.</summary>
        Rollback,
    }

    /// <summary>Marker interface: classes implementing it are scanned for [SetupHook] methods.</summary>
    public interface ISetupHooks
    {
    }

    /// <summary>Marks a public instance or static method taking a <see cref="SetupContext"/> as a hook.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class SetupHookAttribute : Attribute
    {
        public SetupHookAttribute(HookStage stage)
        {
            Stage = stage;
        }

        public HookStage Stage { get; }

        /// <summary>Run with elevated privileges (SYSTEM for per-machine installs). Default true for deferred stages.</summary>
        public bool Elevated { get; set; } = true;

        /// <summary>Log failures and continue instead of failing the installation.</summary>
        public bool ContinueOnError { get; set; }

        /// <summary>Optional Windows Installer condition appended to the stage condition, e.g. "NOT Installed".</summary>
        public string Condition { get; set; }

        /// <summary>Order among hooks of the same stage (lower runs first).</summary>
        public int Order { get; set; }
    }

    public enum UiLevel { None = 2, Basic = 3, Reduced = 4, Full = 5 }

    /// <summary>Everything a hook can know and do. Implemented by the Tenon hook host.</summary>
    public abstract class SetupContext
    {
        public abstract HookStage Stage { get; }
        public abstract string ProductName { get; }
        public abstract Version Version { get; }
        public abstract string InstallDir { get; }
        public abstract bool IsPerMachine { get; }
        public abstract bool IsUpgrade { get; }
        public abstract Version PreviousVersion { get; }
        public abstract bool IsUninstall { get; }
        public abstract bool IsRepair { get; }
        public abstract bool IsElevated { get; }
        public abstract UiLevel UiLevel { get; }
        public abstract IReadOnlyDictionary<string, string> Properties { get; }
        public abstract CancellationToken Cancellation { get; }

        /// <summary>A scratch folder that exists for the duration of the hook run.</summary>
        public abstract string DataDirectory { get; }

        /// <summary>True when the task (checkbox) with this id was selected.</summary>
        public abstract bool Task(string id);

        /// <summary>Sets a Windows Installer property. Only allowed in the Prepare stage.</summary>
        public abstract void SetProperty(string name, string value);

        public abstract void Log(string message);
        public abstract void LogWarning(string message);

        /// <summary>Reports progress text to the setup UI.</summary>
        public abstract void Progress(string status);

        /// <summary>Asks Setup to request a restart when it finishes.</summary>
        public abstract void RequireReboot();
    }

    /// <summary>Throw from a hook to fail the installation with a message shown to the user.</summary>
    public sealed class SetupHookException : Exception
    {
        public SetupHookException(string message) : base(message) { }
        public SetupHookException(string message, Exception inner) : base(message, inner) { }
    }
}
