namespace GameSystems.Management
{
    /// <summary>
    /// Gate for the "[SpawnDebug]" trace added while chasing the spawn-selection/collision bugs.
    /// Call sites do `if (SpawnDebugLog.Enabled) print(...)` directly - flip Enabled to turn the
    /// whole trace back on without touching call sites. Since it's a const, the compiler dead-code
    /// -eliminates the disabled branches entirely (no wasted string interpolation).
    /// </summary>
    public static class SpawnDebugLog
    {
        public const bool Enabled = false;
    }
}
