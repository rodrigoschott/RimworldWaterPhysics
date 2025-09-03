using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// A helper class for conditional logging in the WaterSpring mod
    /// </summary>
    public static class WaterSpringLogger
    {
        private const string LOG_PREFIX = "[WaterSpring] ";
        // Fast-path cache for debug flag to avoid repeated mod lookups on hot paths
        private static volatile bool _debugEnabled;
    // Public fast-path so call sites can avoid string interpolation when disabled
    public static bool DebugEnabled => _debugEnabled;
        
        public static void SetDebugEnabled(bool enabled)
        {
            _debugEnabled = enabled;
        }
        
        /// <summary>
        /// Logs a message if debug mode is enabled
        /// </summary>
        public static void LogDebug(string message)
        {
            if (IsDebugEnabled())
            {
                Log.Message(LOG_PREFIX + message);
            }
        }
        
        /// <summary>
        /// Logs a warning if debug mode is enabled
        /// </summary>
        public static void LogWarning(string message)
        {
            if (IsDebugEnabled())
            {
                Log.Warning(LOG_PREFIX + message);
            }
        }
        
        /// <summary>
        /// Always logs an error, regardless of debug mode
        /// </summary>
        public static void LogError(string message)
        {
            Log.Error(LOG_PREFIX + message);
        }
        
        /// <summary>
        /// Checks if debug mode is enabled in the mod settings
        /// </summary>
        private static bool IsDebugEnabled()
        {
            // Use the cached flag first; this is updated by the mod on load and when settings change
            return _debugEnabled;
        }

        // Optional heavy logging site; only compiled when WATERPHYSICS_VERBOSE is defined
        [System.Diagnostics.Conditional("WATERPHYSICS_VERBOSE")]
        public static void LogVerbose(string message)
        {
            if (IsDebugEnabled())
            {
                Log.Message(LOG_PREFIX + message);
            }
        }
    }
}
