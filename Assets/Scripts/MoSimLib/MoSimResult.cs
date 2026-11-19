using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace MoSimLib
{
    // =========================================================================
    // 1. VERSION REPRESENTATION & SEMANTICS
    // =========================================================================

    public readonly struct MoSimVersion : IComparable<MoSimVersion>, IEquatable<MoSimVersion>
    {
        public int Year { get; }
        public int Minor { get; }
        public int Patch { get; }

        public MoSimVersion(int year, int minor = 0, int patch = 0)
        {
            Year = year;
            Minor = minor;
            Patch = patch;
        }

        public static MoSimVersion Parse(string version)
        {
            if (TryParse(version, out var parsed))
            {
                return parsed;
            }
            throw new FormatException($"String '{version}' was not recognized as a valid Year.Minor.Patch version.");
        }

        public static bool TryParse(string version, out MoSimVersion result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(version)) return false;

            var parts = version.Trim().TrimStart('v', 'V').Split('.');
            if (parts.Length == 0 || !int.TryParse(parts[0], out int year)) return false;

            int minor = parts.Length > 1 && int.TryParse(parts[1], out int m) ? m : 0;
            int patch = parts.Length > 2 && int.TryParse(parts[2], out int p) ? p : 0;

            result = new MoSimVersion(year, minor, patch);
            return true;
        }

        // Implicit conversions
        public static implicit operator MoSimVersion(string version) => Parse(version);
        public static implicit operator MoSimVersion(Version v) => 
            v == null ? default : new MoSimVersion(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

        // Comparisons & Ordering
        public int CompareTo(MoSimVersion other)
        {
            int y = Year.CompareTo(other.Year);
            if (y != 0) return y;
            int m = Minor.CompareTo(other.Minor);
            if (m != 0) return m;
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator <(MoSimVersion left, MoSimVersion right) => left.CompareTo(right) < 0;
        public static bool operator <=(MoSimVersion left, MoSimVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >(MoSimVersion left, MoSimVersion right) => left.CompareTo(right) > 0;
        public static bool operator >=(MoSimVersion left, MoSimVersion right) => left.CompareTo(right) >= 0;
        public static bool operator ==(MoSimVersion left, MoSimVersion right) => left.Equals(right);
        public static bool operator !=(MoSimVersion left, MoSimVersion right) => !left.Equals(right);

        public bool Equals(MoSimVersion other) => Year == other.Year && Minor == other.Minor && Patch == other.Patch;
        public override bool Equals(object obj) => obj is MoSimVersion other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Year, Minor, Patch);
        public override string ToString() => $"{Year}.{Minor}.{Patch}";
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class MoSimApiVersionAttribute : Attribute
    {
        public MoSimVersion Version { get; }

        public MoSimApiVersionAttribute(string version) => Version = MoSimVersion.Parse(version);
        public MoSimApiVersionAttribute(int year, int minor = 0, int patch = 0) => Version = new MoSimVersion(year, minor, patch);
    }

    // =========================================================================
    // 2. OUTCOME CARRIER (Result Type)
    // =========================================================================

    public readonly struct MoSimResult<T>
    {
        private readonly T _value;

        public bool IsSuccess { get; }
        public string Error { get; }

        public T Value => IsSuccess 
            ? _value 
            : throw new InvalidOperationException($"Cannot access {nameof(Value)} on a failed result: {Error}");

        private MoSimResult(T value, bool isSuccess, string error)
        {
            _value = value;
            IsSuccess = isSuccess;
            Error = error;
            if (!isSuccess)
            {
                Debug.LogError(error);
            }
        }

        public static MoSimResult<T> Success(T value) => new(value, true, null);
        public static MoSimResult<T> Fail(string error) => new(default, false, error ?? "Operation failed.");

        public static implicit operator T(MoSimResult<T> result) => result.Value;
    }

    /// <summary>
    /// Result payload used by API operations that do not otherwise produce a value.
    /// </summary>
    public readonly struct Void
    {
    }

    // =========================================================================
    // 3. DISPATCHER & CONTEXT RUNNER
    // =========================================================================

    public static class MoSim
    {
        private static readonly AsyncLocal<MoSimVersion?> ExplicitOverride = new();
        private static readonly ConcurrentDictionary<Assembly, MoSimVersion> AssemblyCache = new();
        private static readonly Assembly EngineAssembly = typeof(MoSim).Assembly;

        /// <summary>
        /// The Unity editor/player version this build targets. This is initialized
        /// from Unity so it advances automatically when the project is upgraded.
        /// </summary>
        public static readonly MoSimVersion LatestVersion = GetUnityVersion();

        /// <summary>
        /// Gets the active API version. Prefers explicit overrides, falls back 
        /// to the calling assembly's target version, or defaults to LatestVersion.
        /// </summary>
        // Runtime API wrappers currently have no version-specific branches. Resolving a caller
        // assembly here required allocating and walking a StackTrace for every wrapper call,
        // including calls made every Update and FixedUpdate. Use the player target version until
        // real version-specific dispatch is introduced at a non-hot call boundary.
        public static MoSimVersion ActiveVersion => LatestVersion;

        public static void WithVersion(Action action, MoSimVersion version)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var prior = ExplicitOverride.Value;
            ExplicitOverride.Value = version;
            try
            {
                action();
            }
            finally
            {
                ExplicitOverride.Value = prior;
            }
        }

        public static TResult WithVersion<TResult>(Func<TResult> func, MoSimVersion version)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            var prior = ExplicitOverride.Value;
            ExplicitOverride.Value = version;
            try
            {
                return func();
            }
            finally
            {
                ExplicitOverride.Value = prior;
            }
        }

        private static MoSimVersion GetUnityVersion()
        {
            var components = UnityEngine.Application.unityVersion.Split('.');
            return new MoSimVersion(
                ParseUnityVersionComponent(components, 0),
                ParseUnityVersionComponent(components, 1),
                ParseUnityVersionComponent(components, 2));
        }

        private static int ParseUnityVersionComponent(string[] components, int index)
        {
            if (components == null || index >= components.Length) return 0;

            var component = components[index];
            var length = 0;
            while (length < component.Length && char.IsDigit(component[length])) length++;

            return length > 0 && int.TryParse(component.Substring(0, length), out var value) ? value : 0;
        }

        private static MoSimVersion ResolveCallingAssemblyVersion()
        {
            var stackTrace = new StackTrace(skipFrames: 2, fNeedFileInfo: false);
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                var method = stackTrace.GetFrame(i)?.GetMethod();
                var callerAssembly = method?.DeclaringType?.Assembly;

                // Stop at the first assembly outside MoSimLib
                if (callerAssembly != null && callerAssembly != EngineAssembly)
                {
                    return AssemblyCache.GetOrAdd(callerAssembly, InspectAssemblyVersion);
                }
            }

            return LatestVersion;
        }

        private static MoSimVersion InspectAssemblyVersion(Assembly assembly)
        {
            // Priority 1: Check for explicit attribute [assembly: MoSimApiVersion(...)]
            var attr = assembly.GetCustomAttribute<MoSimApiVersionAttribute>();
            if (attr != null)
            {
                return attr.Version;
            }

            // Priority 2: Check referenced MoSimLib AssemblyVersion
            var reference = Array.Find(
                assembly.GetReferencedAssemblies(),
                an => an.Name == EngineAssembly.GetName().Name
            );

            if (reference?.Version != null)
            {
                return (MoSimVersion)reference.Version;
            }

            return LatestVersion;
        }
    }
}
