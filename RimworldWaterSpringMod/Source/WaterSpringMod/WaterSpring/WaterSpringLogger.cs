using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// A helper class for conditional logging in the WaterSpring mod
    /// </summary>
    public static class WaterSpringLogger
    {
        private const string LOG_PREFIX = "[WaterSpring] ";
        
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
            WaterSpringModMain modMain = LoadedModManager.GetMod<WaterSpringModMain>();
            if (modMain != null)
            {
                WaterSpringModSettings settings = modMain.GetModSettings();
                return settings != null && settings.debugModeEnabled;
            }
            return false;
        }
    }
}
