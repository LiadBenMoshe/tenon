namespace Tenon.Core.Model;

public enum InstallScope { PerMachine, PerUser, Either }

public enum Architecture { X64, X86, Arm64 }

public enum CloseRunningAppMode { Prompt, Force, Fail }

public enum RegistryRoot { HKMU, HKLM, HKCU, HKCR, HKU }

public enum RegistryValueType { String, ExpandString, Dword, Qword, Binary, MultiString }

public enum EnvironmentAction { Set, Append, Prepend, Remove }

public enum EnvironmentScope { Auto, User, Machine }

public enum ServiceStartMode { Auto, Demand, Disabled, DelayedAuto }

public enum FirewallDirection { In, Out }

public enum FirewallProtocol { Tcp, Udp, Any }

public enum UiStyle { Modern, OneClick }

public enum SigningMode { None, Signtool, Command }

public enum ScopeRequirement { Any, PerMachine, PerUser }
