using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using Rewired;
using TheForest.UI;
using TheForest.UI.Multiplayer;
using TheForest.Utils;
using UnityEngine;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Handles remapping all original 'E' gameplay interactions ("Take", "Build", and any other E-bound
    /// gameplay actions in Rewired) to 'V' (or the configured InteractionKey), updates all in-game UI
    /// interaction prompts/icons to display 'V', prevents saving temporary mod overrides into PlayerPrefs,
    /// and cleanly restores original bindings when disabled or unloaded.
    /// </summary>
    public sealed class InputRemapManager
    {
        private sealed class SavedElementBinding
        {
            public ControllerMap Map;
            public int ElementMapId;
            public int ActionId;
            public string ActionName;
            public Pole AxisContribution;
            public KeyCode OriginalKeyCode;
            public ModifierKey Modifier1;
            public ModifierKey Modifier2;
            public ModifierKey Modifier3;
        }

        public static InputRemapManager Instance { get; private set; }

        private readonly ModConfig _config;
        private readonly List<SavedElementBinding> _originalBindings = new List<SavedElementBinding>();
        private bool _isApplied;
        private KeyCode _appliedDropkickKey = KeyCode.None;
        private KeyCode _appliedInteractionKey = KeyCode.None;
        private float _nextCheckTime;
        private bool _temporarilyRestoredForSave;

        // Track V press start time for GetButtonPress fallback
        private float _interactionKeyDownTime;

        public InputRemapManager(ModConfig config)
        {
            _config = config;
            Instance = this;
        }

        /// <summary>
        /// Called every frame by the mod runner to ensure Rewired mappings and UI icons stay synchronized
        /// across scene loads (main menu -> plane crash -> world -> caves).
        /// </summary>
        public void Update()
        {
            if (UnityEngine.Input.GetKeyDown(_config.InteractionKey))
            {
                _interactionKeyDownTime = Time.unscaledTime;
            }

            if (!_config.DropkickEnabled)
            {
                if (_isApplied)
                {
                    RestoreOriginalBindings();
                }
                return;
            }

            if (_appliedDropkickKey != _config.DropkickKey || _appliedInteractionKey != _config.InteractionKey)
            {
                if (_isApplied)
                {
                    RestoreOriginalBindings();
                }
                ApplyBindings();
                return;
            }

            if (Time.unscaledTime >= _nextCheckTime)
            {
                _nextCheckTime = Time.unscaledTime + 1.0f;
                EnsureBindingsApplied();
            }
        }

        /// <summary>
        /// Checks if Rewired player & keyboard maps are ready and applies E -> V remapping if needed.
        /// </summary>
        public void EnsureBindingsApplied()
        {
            if (!_config.DropkickEnabled || _temporarilyRestoredForSave)
                return;

            Player rewiredPlayer = TheForest.Utils.Input.player;
            if (rewiredPlayer == null || !ReInput.isReady)
                return;

            bool needsReapply = !_isApplied;
            if (!needsReapply)
            {
                // Verify whether any keyboard map still has DropkickKey (E) assigned to Take or Build
                IEnumerable<ControllerMap> maps = rewiredPlayer.controllers.maps.GetAllMaps(ControllerType.Keyboard);
                if (maps != null)
                {
                    foreach (ControllerMap map in maps)
                    {
                        if (map == null) continue;
                        foreach (ActionElementMap aem in map.AllMaps)
                        {
                            if (aem != null && aem.keyCode == _config.DropkickKey && IsRemappableAction(aem.actionId))
                            {
                                needsReapply = true;
                                break;
                            }
                        }
                        if (needsReapply) break;
                    }
                }
            }

            if (needsReapply)
            {
                ApplyBindings();
            }
            else
            {
                RefreshUiPromptLabels();
            }
        }

        private bool IsRemappableAction(int actionId)
        {
            if (!ReInput.isReady || ReInput.mapping == null)
                return true;

            InputAction action = ReInput.mapping.GetAction(actionId);
            if (action == null)
                return false;

            string name = action.name;
            if (string.IsNullOrEmpty(name))
                return false;

            // "Take" and "Build" are the primary original E actions in The Forest, plus any other E-bound gameplay action
            if (string.Equals(name, "Take", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Build", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Interact", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Use", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Also remap any other action that originally used E (DropkickKey) so E never triggers old actions
            return true;
        }

        public void ApplyBindings()
        {
            try
            {
                if (!ReInput.isReady)
                    return;

                Player rewiredPlayer = TheForest.Utils.Input.player;
                if (rewiredPlayer == null)
                {
                    if (ReInput.players != null && ReInput.players.playerCount > 0)
                        rewiredPlayer = ReInput.players.GetPlayer(0);
                    if (rewiredPlayer == null)
                        return;
                }

                IEnumerable<ControllerMap> keyboardMaps = rewiredPlayer.controllers.maps.GetAllMaps(ControllerType.Keyboard);
                if (keyboardMaps == null)
                    return;

                KeyCode sourceKey = _config.DropkickKey;
                KeyCode targetKey = _config.InteractionKey;
                int replacedCount = 0;

                foreach (ControllerMap map in keyboardMaps)
                {
                    if (map == null)
                        continue;

                    // Collect snapshot of ActionElementMaps before modifying
                    List<ActionElementMap> elements = new List<ActionElementMap>(map.AllMaps);
                    for (int i = 0; i < elements.Count; i++)
                    {
                        ActionElementMap aem = elements[i];
                        if (aem == null)
                            continue;

                        InputAction action = ReInput.mapping.GetAction(aem.actionId);
                        string actionName = action != null ? action.name : string.Empty;

                        bool isTakeOrBuild =
                            string.Equals(actionName, "Take", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(actionName, "Build", StringComparison.OrdinalIgnoreCase);

                        // Remap if bound to DropkickKey (E), or if it's Take/Build bound to E
                        if (aem.keyCode == sourceKey || (isTakeOrBuild && aem.keyCode != targetKey && aem.keyCode == KeyCode.E))
                        {
                            RecordOriginalBindingIfMissing(map, aem, actionName);

                            bool ok = map.ReplaceElementMap(
                                aem.id,
                                aem.actionId,
                                aem.axisContribution,
                                targetKey,
                                aem.modifierKey1,
                                aem.modifierKey2,
                                aem.modifierKey3);

                            if (ok)
                            {
                                replacedCount++;
                                DebugLogger.Debug("Remapped Rewired action '" + actionName + "' (id=" + aem.actionId +
                                                  ", elementId=" + aem.id + ") from " + sourceKey + " to " + targetKey);
                            }
                        }
                    }
                }

                _isApplied = true;
                _appliedDropkickKey = sourceKey;
                _appliedInteractionKey = targetKey;

                RefreshUiPromptLabels();

                if (replacedCount > 0)
                {
                    DebugLogger.Info("Remapped " + replacedCount + " Rewired keybinding(s): '" +
                                     sourceKey + "' -> '" + targetKey + "' (Take/Build/Interactions).");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Error applying Rewired keybindings: " + ex);
            }
        }

        private void RecordOriginalBindingIfMissing(ControllerMap map, ActionElementMap aem, string actionName)
        {
            for (int i = 0; i < _originalBindings.Count; i++)
            {
                SavedElementBinding existing = _originalBindings[i];
                if (existing.Map == map && existing.ElementMapId == aem.id)
                    return;
            }

            _originalBindings.Add(new SavedElementBinding
            {
                Map = map,
                ElementMapId = aem.id,
                ActionId = aem.actionId,
                ActionName = actionName,
                AxisContribution = aem.axisContribution,
                OriginalKeyCode = aem.keyCode,
                Modifier1 = aem.modifierKey1,
                Modifier2 = aem.modifierKey2,
                Modifier3 = aem.modifierKey3
            });
        }

        public void RestoreOriginalBindings()
        {
            try
            {
                int restoredCount = 0;
                for (int i = 0; i < _originalBindings.Count; i++)
                {
                    SavedElementBinding b = _originalBindings[i];
                    if (b.Map == null)
                        continue;

                    bool ok = b.Map.ReplaceElementMap(
                        b.ElementMapId,
                        b.ActionId,
                        b.AxisContribution,
                        b.OriginalKeyCode,
                        b.Modifier1,
                        b.Modifier2,
                        b.Modifier3);

                    if (ok)
                        restoredCount++;
                }

                _originalBindings.Clear();
                _isApplied = false;
                _appliedDropkickKey = KeyCode.None;
                _appliedInteractionKey = KeyCode.None;

                string[] kbMappings = GameReflection.GetKeyboardMappings();
                if (kbMappings != null)
                {
                    InputMappingIcons.RefreshMappings();
                    UpdateActiveWorldIcons();
                }

                if (restoredCount > 0)
                {
                    DebugLogger.Info("Restored " + restoredCount + " original Rewired keybinding(s).");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Error restoring original Rewired keybindings: " + ex);
            }
        }

        public void RefreshUiPromptLabels()
        {
            try
            {
                string[] kbMappings = GameReflection.GetKeyboardMappings();
                if (kbMappings != null)
                {
                    InputMappingIcons.RefreshMappings();
                    kbMappings = GameReflection.GetKeyboardMappings();

                    string keyLabel = _config.InteractionKey.ToString();
                    int takeIdx = (int)InputMappingIcons.Actions.Take;
                    int buildIdx = (int)InputMappingIcons.Actions.Build;

                    if (kbMappings != null)
                    {
                        if (takeIdx >= 0 && takeIdx < kbMappings.Length)
                            kbMappings[takeIdx] = keyLabel;

                        if (buildIdx >= 0 && buildIdx < kbMappings.Length)
                            kbMappings[buildIdx] = keyLabel;
                    }

                    InputMappingIcons.Version++;
                }

                UpdateActiveWorldIcons();
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("RefreshUiPromptLabels non-fatal exception: " + ex.Message);
            }
        }

        private void UpdateActiveWorldIcons()
        {
            Dictionary<Transform, ActionIcon> dict = GameReflection.GetActiveIcons();
            if (dict == null)
                return;

            foreach (KeyValuePair<Transform, ActionIcon> kvp in dict)
            {
                ActionIcon icon = kvp.Value;
                if (icon == null || icon._label == null)
                    continue;

                if (icon._action == InputMappingIcons.Actions.Take || icon._action == InputMappingIcons.Actions.Build)
                {
                    string expected = _config.DropkickEnabled
                        ? _config.InteractionKey.ToString()
                        : InputMappingIcons.GetMappingFor(icon._action);

                    if (!string.IsNullOrEmpty(expected) && icon._label.text != expected)
                    {
                        icon._label.text = expected;
                    }
                }
            }
        }

        public static bool IsGameplayInputContextActive()
        {
            if (Instance == null || !Instance._config.DropkickEnabled)
                return false;

            // Never interfere when typing in multiplayer chat
            if (ChatBox.IsChatOpen)
                return false;

            // Never interfere if an NGUI text input has focus
            if (UICamera.inputHasFocus)
                return false;

            return true;
        }

        public static bool IsRemappedInteractionButton(string button)
        {
            if (string.IsNullOrEmpty(button))
                return false;

            return string.Equals(button, "Take", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(button, "Build", StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        // Harmony Patches for TheForest.Utils.Input, InputMapping, and PlayMaker
        // =====================================================================

        [HarmonyPatch(typeof(TheForest.Utils.Input), "GetButtonDown", new Type[] { typeof(string) })]
        public static class Patch_Input_GetButtonDown
        {
            public static void Postfix(string button, ref bool __result)
            {
                if (!IsGameplayInputContextActive() || !IsRemappedInteractionButton(button))
                    return;

                KeyCode interactKey = Instance._config.InteractionKey;
                KeyCode dropkickKey = Instance._config.DropkickKey;

                // Ensure V triggers Take/Build and E never triggers Take/Build
                if (UnityEngine.Input.GetKeyDown(interactKey))
                {
                    __result = true;
                }
                else if (__result && UnityEngine.Input.GetKeyDown(dropkickKey) && !UnityEngine.Input.GetKeyDown(interactKey))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(TheForest.Utils.Input), "GetButton", new Type[] { typeof(string) })]
        public static class Patch_Input_GetButton
        {
            public static void Postfix(string button, ref bool __result)
            {
                if (!IsGameplayInputContextActive() || !IsRemappedInteractionButton(button))
                    return;

                KeyCode interactKey = Instance._config.InteractionKey;
                KeyCode dropkickKey = Instance._config.DropkickKey;

                if (UnityEngine.Input.GetKey(interactKey))
                {
                    __result = true;
                }
                else if (__result && UnityEngine.Input.GetKey(dropkickKey) && !UnityEngine.Input.GetKey(interactKey))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(TheForest.Utils.Input), "GetButtonUp", new Type[] { typeof(string) })]
        public static class Patch_Input_GetButtonUp
        {
            public static void Postfix(string button, ref bool __result)
            {
                if (!IsGameplayInputContextActive() || !IsRemappedInteractionButton(button))
                    return;

                KeyCode interactKey = Instance._config.InteractionKey;
                KeyCode dropkickKey = Instance._config.DropkickKey;

                if (UnityEngine.Input.GetKeyUp(interactKey))
                {
                    __result = true;
                }
                else if (__result && UnityEngine.Input.GetKeyUp(dropkickKey) && !UnityEngine.Input.GetKeyUp(interactKey))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(TheForest.Utils.Input), "GetButtonPress", new Type[] { typeof(string) })]
        public static class Patch_Input_GetButtonPress
        {
            public static void Postfix(string button, ref bool __result)
            {
                if (!IsGameplayInputContextActive() || !IsRemappedInteractionButton(button))
                    return;

                KeyCode interactKey = Instance._config.InteractionKey;
                KeyCode dropkickKey = Instance._config.DropkickKey;

                if (UnityEngine.Input.GetKeyUp(interactKey) && (Time.unscaledTime - Instance._interactionKeyDownTime) < 0.25f)
                {
                    __result = true;
                }
                else if (__result && UnityEngine.Input.GetKeyUp(dropkickKey) && !UnityEngine.Input.GetKeyUp(interactKey))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(InputMapping), "LoadAllMaps")]
        public static class Patch_InputMapping_LoadAllMaps
        {
            public static void Postfix()
            {
                if (Instance != null && Instance._config.DropkickEnabled)
                {
                    Instance._isApplied = false;
                    Instance.ApplyBindings();
                }
            }
        }

        [HarmonyPatch(typeof(InputMapping), "SaveAllMaps")]
        public static class Patch_InputMapping_SaveAllMaps
        {
            public static void Prefix()
            {
                // Restore original bindings before saving to PlayerPrefs so uninstalling the mod
                // leaves the user's saved controls completely clean.
                if (Instance != null && Instance._isApplied)
                {
                    Instance._temporarilyRestoredForSave = true;
                    Instance.RestoreOriginalBindings();
                }
            }

            public static void Postfix()
            {
                if (Instance != null && Instance._temporarilyRestoredForSave)
                {
                    Instance._temporarilyRestoredForSave = false;
                    if (Instance._config.DropkickEnabled)
                    {
                        Instance.ApplyBindings();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(InputMappingIcons), "RefreshMappings")]
        public static class Patch_InputMappingIcons_RefreshMappings
        {
            public static void Postfix()
            {
                if (Instance != null && Instance._config.DropkickEnabled)
                {
                    string keyLabel = Instance._config.InteractionKey.ToString();
                    string[] kbMappings = GameReflection.GetKeyboardMappings();
                    if (kbMappings != null)
                    {
                        int takeIdx = (int)InputMappingIcons.Actions.Take;
                        int buildIdx = (int)InputMappingIcons.Actions.Build;
                        if (takeIdx >= 0 && takeIdx < kbMappings.Length)
                            kbMappings[takeIdx] = keyLabel;
                        if (buildIdx >= 0 && buildIdx < kbMappings.Length)
                            kbMappings[buildIdx] = keyLabel;
                    }
                }
            }
        }

        // PlayMaker GetKey / GetKeyDown / GetKeyUp patches in case any FSM checks KeyCode.E directly
        [HarmonyPatch(typeof(GetKey), "DoGetKey")]
        public static class Patch_PlayMaker_GetKey
        {
            public static void Prefix(GetKey __instance, ref KeyCode __state)
            {
                __state = KeyCode.None;
                if (IsGameplayInputContextActive() && __instance.key == Instance._config.DropkickKey)
                {
                    __state = __instance.key;
                    __instance.key = Instance._config.InteractionKey;
                }
            }

            public static void Postfix(GetKey __instance, KeyCode __state)
            {
                if (__state != KeyCode.None)
                {
                    __instance.key = __state;
                }
            }
        }

        [HarmonyPatch(typeof(GetKeyDown), "OnUpdate")]
        public static class Patch_PlayMaker_GetKeyDown
        {
            public static void Prefix(GetKeyDown __instance, ref KeyCode __state)
            {
                __state = KeyCode.None;
                if (IsGameplayInputContextActive() && __instance.key == Instance._config.DropkickKey)
                {
                    __state = __instance.key;
                    __instance.key = Instance._config.InteractionKey;
                }
            }

            public static void Postfix(GetKeyDown __instance, KeyCode __state)
            {
                if (__state != KeyCode.None)
                {
                    __instance.key = __state;
                }
            }
        }

        [HarmonyPatch(typeof(GetKeyUp), "OnUpdate")]
        public static class Patch_PlayMaker_GetKeyUp
        {
            public static void Prefix(GetKeyUp __instance, ref KeyCode __state)
            {
                __state = KeyCode.None;
                if (IsGameplayInputContextActive() && __instance.key == Instance._config.DropkickKey)
                {
                    __state = __instance.key;
                    __instance.key = Instance._config.InteractionKey;
                }
            }

            public static void Postfix(GetKeyUp __instance, KeyCode __state)
            {
                if (__state != KeyCode.None)
                {
                    __instance.key = __state;
                }
            }
        }
    }
}
