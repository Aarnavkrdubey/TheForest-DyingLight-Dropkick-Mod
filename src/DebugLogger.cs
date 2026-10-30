using System;
using BepInEx.Logging;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Centralized logging helper for the Dying Light Dropkick Mod.
    /// Verbose debug messages are only emitted when DebugLogging is enabled in configuration.
    /// </summary>
    public static class DebugLogger
    {
        private static ManualLogSource _logSource;
        private static ModConfig _config;

        public static void Initialize(ManualLogSource logSource, ModConfig config)
        {
            _logSource = logSource;
            _config = config;
        }

        public static void Info(string message)
        {
            if (_logSource != null)
            {
                _logSource.LogInfo(message);
            }
        }

        public static void Warn(string message)
        {
            if (_logSource != null)
            {
                _logSource.LogWarning(message);
            }
        }

        public static void Error(string message)
        {
            if (_logSource != null)
            {
                _logSource.LogError(message);
            }
        }

        public static void Debug(string message)
        {
            if (_logSource != null && _config != null && _config.DebugLogging)
            {
                _logSource.LogInfo("[DEBUG] " + message);
            }
        }
    }
}
