using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace TheForestDropkickMod
{
    public enum CameraShakeIntensity
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    /// <summary>
    /// Manages all user-configurable settings for the Dying Light-Style Dropkick Mod.
    /// Supports both BepInEx's standard configuration system and live hot-reloading.
    /// Validates keybindings so DropkickKey and InteractionKey never conflict.
    /// </summary>
    public sealed class ModConfig
    {
        public ConfigFile BackingFile { get; private set; }

        // 1. Keybindings
        public ConfigEntry<KeyCode> DropkickKeyEntry { get; private set; }
        public ConfigEntry<KeyCode> InteractionKeyEntry { get; private set; }

        // 2. Core Toggles & Balancing
        public ConfigEntry<bool> DropkickEnabledEntry { get; private set; }
        public ConfigEntry<bool> SprintMomentumBonusEntry { get; private set; }
        public ConfigEntry<float> DropkickDamageEntry { get; private set; }
        public ConfigEntry<float> DropkickForceEntry { get; private set; }
        public ConfigEntry<float> EnemyKnockbackForceEntry { get; private set; }
        public ConfigEntry<float> HeavyMutantKnockbackMultiplierEntry { get; private set; }
        public ConfigEntry<float> DropkickDurationEntry { get; private set; }
        public ConfigEntry<float> CooldownEntry { get; private set; }
        public ConfigEntry<bool> StaminaConsumptionEntry { get; private set; }
        public ConfigEntry<float> StaminaCostEntry { get; private set; }

        // 3. Camera & Debug
        public ConfigEntry<CameraShakeIntensity> CameraShakeEntry { get; private set; }
        public ConfigEntry<bool> DebugLoggingEntry { get; private set; }

        // Resolved safe runtime values
        public KeyCode DropkickKey { get; private set; }
        public KeyCode InteractionKey { get; private set; }
        public bool DropkickEnabled { get; private set; }
        public bool SprintMomentumBonus { get; private set; }
        public float DropkickDamage { get; private set; }
        public float DropkickForce { get; private set; }
        public float EnemyKnockbackForce { get; private set; }
        public float HeavyMutantKnockbackMultiplier { get; private set; }
        public float DropkickDuration { get; private set; }
        public float Cooldown { get; private set; }
        public bool StaminaConsumption { get; private set; }
        public float StaminaCost { get; private set; }
        public CameraShakeIntensity CameraShake { get; private set; }
        public bool DebugLogging { get; private set; }

        public event Action OnConfigChanged;

        private FileSystemWatcher _watcher;
        private float _lastReloadTime;

        public ModConfig(ConfigFile config)
        {
            BackingFile = config;
            BindEntries();
            ValidateAndCacheValues();
            SetupFileWatcher();
        }

        private void BindEntries()
        {
            DropkickKeyEntry = BackingFile.Bind(
                "1. Controls",
                "DropkickKey",
                KeyCode.E,
                "Key used while airborne to execute the two-legged Dying Light dropkick. Default: E");

            InteractionKeyEntry = BackingFile.Bind(
                "1. Controls",
                "InteractionKey",
                KeyCode.V,
                "Key that replaces all original E interactions (picking up items, opening doors, adding building materials, climbing ropes, boats/rafts, animals, etc.). Default: V");

            DropkickEnabledEntry = BackingFile.Bind(
                "2. Mechanics",
                "DropkickEnabled",
                true,
                "Master toggle for the dropkick mechanic and E->V interaction remapping. Default: true");

            SprintMomentumBonusEntry = BackingFile.Bind(
                "2. Mechanics",
                "SprintMomentumBonus",
                true,
                "When enabled, sprinting before jumping increases dropkick forward velocity boost, physical damage, and enemy knockback. Default: true");

            DropkickDamageEntry = BackingFile.Bind(
                "3. Combat & Physics",
                "DropkickDamage",
                42f,
                new ConfigDescription(
                    "Base physical damage applied to enemies hit by the dropkick (scales up by +35% when sprinting).",
                    new AcceptableValueRange<float>(5f, 250f)));

            DropkickForceEntry = BackingFile.Bind(
                "3. Combat & Physics",
                "DropkickForce",
                7.2f,
                new ConfigDescription(
                    "Forward velocity boost (m/s) applied to the player when executing the dropkick (scales up to ~8.8 m/s when sprinting). Recommended range: 6.0 - 9.0 m/s.",
                    new AcceptableValueRange<float>(4f, 14f)));

            EnemyKnockbackForceEntry = BackingFile.Bind(
                "3. Combat & Physics",
                "EnemyKnockbackForce",
                10.5f,
                new ConfigDescription(
                    "Physical knockback impulse speed (m/s) applied to cannibals on impact (scales with player momentum).",
                    new AcceptableValueRange<float>(2f, 25f)));

            HeavyMutantKnockbackMultiplierEntry = BackingFile.Bind(
                "3. Combat & Physics",
                "HeavyMutantKnockbackMultiplier",
                0.32f,
                new ConfigDescription(
                    "Knockback multiplier for heavy creepy mutants (Armsy, Virginia, Cowman) appropriate for their larger mass.",
                    new AcceptableValueRange<float>(0.05f, 1.0f)));

            DropkickDurationEntry = BackingFile.Bind(
                "2. Mechanics",
                "DropkickDuration",
                0.54f,
                new ConfigDescription(
                    "Total duration (in seconds) of the airborne dropkick attack (startup + active impact + recovery). Recommended: 0.4 - 0.7 seconds.",
                    new AcceptableValueRange<float>(0.35f, 0.85f)));

            CooldownEntry = BackingFile.Bind(
                "2. Mechanics",
                "Cooldown",
                1.2f,
                new ConfigDescription(
                    "Minimum cooldown time (in seconds) between dropkicks. Default: 1.2 seconds.",
                    new AcceptableValueRange<float>(0.2f, 10f)));

            StaminaConsumptionEntry = BackingFile.Bind(
                "2. Mechanics",
                "StaminaConsumption",
                true,
                "Whether performing a dropkick consumes player stamina. Default: true");

            StaminaCostEntry = BackingFile.Bind(
                "2. Mechanics",
                "StaminaCost",
                16f,
                new ConfigDescription(
                    "Amount of stamina consumed per dropkick activation when StaminaConsumption is enabled.",
                    new AcceptableValueRange<float>(0f, 60f)));

            CameraShakeEntry = BackingFile.Bind(
                "4. Camera & Feedback",
                "CameraShake",
                CameraShakeIntensity.Low,
                "First-person camera shake intensity on dropkick impact (Off, Low, Medium, High). Default: Low");

            DebugLoggingEntry = BackingFile.Bind(
                "5. Debugging",
                "DebugLogging",
                false,
                "Enable detailed diagnostic logging in BepInEx/LogOutput.log. Default: false");

            BackingFile.SettingChanged += OnSettingChangedInternal;
        }

        private void OnSettingChangedInternal(object sender, SettingChangedEventArgs e)
        {
            ValidateAndCacheValues();
            if (OnConfigChanged != null)
            {
                OnConfigChanged.Invoke();
            }
        }

        public void ValidateAndCacheValues()
        {
            DebugLogging = DebugLoggingEntry.Value;
            DropkickEnabled = DropkickEnabledEntry.Value;
            SprintMomentumBonus = SprintMomentumBonusEntry.Value;
            DropkickDamage = Mathf.Clamp(DropkickDamageEntry.Value, 5f, 250f);
            DropkickForce = Mathf.Clamp(DropkickForceEntry.Value, 4f, 14f);
            EnemyKnockbackForce = Mathf.Clamp(EnemyKnockbackForceEntry.Value, 2f, 25f);
            HeavyMutantKnockbackMultiplier = Mathf.Clamp(HeavyMutantKnockbackMultiplierEntry.Value, 0.05f, 1.0f);
            DropkickDuration = Mathf.Clamp(DropkickDurationEntry.Value, 0.35f, 0.85f);
            Cooldown = Mathf.Clamp(CooldownEntry.Value, 0.2f, 10f);
            StaminaConsumption = StaminaConsumptionEntry.Value;
            StaminaCost = Mathf.Clamp(StaminaCostEntry.Value, 0f, 60f);
            CameraShake = CameraShakeEntry.Value;

            KeyCode desiredDropkick = DropkickKeyEntry.Value;
            KeyCode desiredInteract = InteractionKeyEntry.Value;

            if (desiredDropkick == KeyCode.None)
            {
                DebugLogger.Warn("DropkickKey was set to None; restoring default KeyCode.E.");
                desiredDropkick = KeyCode.E;
            }

            if (desiredInteract == KeyCode.None)
            {
                DebugLogger.Warn("InteractionKey was set to None; restoring default KeyCode.V.");
                desiredInteract = KeyCode.V;
            }

            // Prevent conflict with Jump (Space) or Sprint (LeftShift)
            if (desiredDropkick == KeyCode.Space || desiredDropkick == KeyCode.LeftShift || desiredDropkick == KeyCode.Escape)
            {
                DebugLogger.Warn("DropkickKey (" + desiredDropkick + ") conflicts with core movement/menu controls. Reverting DropkickKey to E.");
                desiredDropkick = KeyCode.E;
            }

            if (desiredInteract == KeyCode.Space || desiredInteract == KeyCode.LeftShift || desiredInteract == KeyCode.Escape)
            {
                DebugLogger.Warn("InteractionKey (" + desiredInteract + ") conflicts with core movement/menu controls. Reverting InteractionKey to V.");
                desiredInteract = KeyCode.V;
            }

            // Prevent DropkickKey and InteractionKey from conflicting with each other
            if (desiredDropkick == desiredInteract)
            {
                KeyCode fallbackInteract = (desiredDropkick == KeyCode.V) ? KeyCode.F : KeyCode.V;
                DebugLogger.Warn("DropkickKey and InteractionKey were both set to " + desiredDropkick +
                                 ". Automatically resolving conflict: InteractionKey set to " + fallbackInteract + ".");
                desiredInteract = fallbackInteract;
            }

            DropkickKey = desiredDropkick;
            InteractionKey = desiredInteract;
        }

        private void SetupFileWatcher()
        {
            try
            {
                string configPath = BackingFile.ConfigFilePath;
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                    return;

                string dir = Path.GetDirectoryName(configPath);
                string file = Path.GetFileName(configPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
                    return;

                _watcher = new FileSystemWatcher(dir, file);
                _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                _watcher.Changed += OnConfigFileModifiedOnDisk;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("Could not initialize config FileSystemWatcher: " + ex.Message);
            }
        }

        private void OnConfigFileModifiedOnDisk(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (Time.realtimeSinceStartup - _lastReloadTime < 0.5f)
                    return;
                _lastReloadTime = Time.realtimeSinceStartup;

                BackingFile.Reload();
                ValidateAndCacheValues();
                if (OnConfigChanged != null)
                {
                    OnConfigChanged.Invoke();
                }
                DebugLogger.Info("Reloaded configuration from disk.");
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("Failed to reload config file: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}
