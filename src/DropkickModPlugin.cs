using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TheForest.UI;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Main BepInEx plugin entry point for the Dying Light-Style Dropkick Mod for The Forest (2018).
    /// Coordinates configuration, Harmony runtime patches, Rewired E->V input remapping,
    /// the airborne dropkick state machine, procedural two-legged skeletal animation,
    /// collision-based hit detection, enemy knockdown/knockback physics, and camera/audio feedback.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class DropkickModPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.theforest.dyinglightdropkick";
        public const string PluginName = "Dying Light Dropkick Mod";
        public const string PluginVersion = "1.0.0";

        public static DropkickModPlugin Instance { get; private set; }

        private Harmony _harmony;
        private ModConfig _modConfig;
        private InputRemapManager _inputRemapManager;
        private DropkickStateMachine _stateMachine;
        private DropkickAnimator _animator;
        private DropkickCameraFeedback _cameraFeedback;
        private DropkickHitDetector _hitDetector;
        private EnemyCombatIntegration _combatIntegration;

        private bool _loggedPlayerReadyVerification;
        private bool _ranStartupSelfTest;

        private void Awake()
        {
            Instance = this;

            try
            {
                _modConfig = new ModConfig(Config);
                DebugLogger.Initialize(Logger, _modConfig);

                DebugLogger.Info("================================================================");
                DebugLogger.Info(PluginName + " v" + PluginVersion + " initializing...");
                DebugLogger.Info("Dropkick Key     : " + _modConfig.DropkickKey);
                DebugLogger.Info("Interaction Key  : " + _modConfig.InteractionKey + " (replaces original E actions)");
                DebugLogger.Info("Dropkick Enabled : " + _modConfig.DropkickEnabled);
                DebugLogger.Info("Sprint Bonus     : " + _modConfig.SprintMomentumBonus);
                DebugLogger.Info("Base Damage      : " + _modConfig.DropkickDamage);
                DebugLogger.Info("Forward Boost    : " + _modConfig.DropkickForce + " m/s");
                DebugLogger.Info("Enemy Knockback  : " + _modConfig.EnemyKnockbackForce + " m/s");
                DebugLogger.Info("Duration / CD    : " + _modConfig.DropkickDuration + "s / " + _modConfig.Cooldown + "s");
                DebugLogger.Info("Stamina Cost     : " + (_modConfig.StaminaConsumption ? _modConfig.StaminaCost.ToString("F1") : "Disabled"));
                DebugLogger.Info("Camera Shake     : " + _modConfig.CameraShake);
                DebugLogger.Info("================================================================");

                _inputRemapManager = new InputRemapManager(_modConfig);
                _stateMachine = new DropkickStateMachine(_modConfig);
                _animator = new DropkickAnimator(_stateMachine);
                _cameraFeedback = new DropkickCameraFeedback(_modConfig, _stateMachine);
                _hitDetector = new DropkickHitDetector();
                _combatIntegration = new EnemyCombatIntegration(_modConfig, this);

                _modConfig.OnConfigChanged += OnModConfigChanged;

                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                DebugLogger.Info("Harmony runtime patches applied successfully.");

                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            catch (Exception ex)
            {
                Logger.LogError("Fatal error initializing " + PluginName + ": " + ex);
            }
        }

        private void OnModConfigChanged()
        {
            if (_inputRemapManager != null)
            {
                _inputRemapManager.EnsureBindingsApplied();
            }
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            _loggedPlayerReadyVerification = false;
            DebugLogger.Debug("Scene loaded: " + scene.name + " (mode=" + mode + ")");
            if (_inputRemapManager != null && _modConfig.DropkickEnabled)
            {
                _inputRemapManager.EnsureBindingsApplied();
            }
        }

        private void Update()
        {
            if (_inputRemapManager != null)
            {
                _inputRemapManager.Update();
            }

            if (_stateMachine != null)
            {
                _stateMachine.Update(_hitDetector, _cameraFeedback, _combatIntegration);
            }

            if (!_ranStartupSelfTest && Rewired.ReInput.isReady && TheForest.Utils.Input.player != null && GameReflection.GetKeyboardMappings() != null)
            {
                _ranStartupSelfTest = true;
                RuntimeSelfTest.RunVerificationSuite(_modConfig, _inputRemapManager, _stateMachine, _animator);
            }

            if (!_loggedPlayerReadyVerification && LocalPlayer.Transform != null && LocalPlayer.ScriptSetup != null)
            {
                _loggedPlayerReadyVerification = true;
                RunInGameVerificationCheck();
            }
        }

        private void LateUpdate()
        {
            // Fallback call in case playerAnimatorControl.LateUpdate or camFollowHead.LateUpdate did not run this frame
            if (_animator != null && _stateMachine != null && _stateMachine.IsDropkicking)
            {
                _animator.ApplyPoseLateUpdate();
            }
        }

        private void RunInGameVerificationCheck()
        {
            try
            {
                bool bonesOk = _animator != null && _animator.EnsureBonesResolved();
                string[] kbMappings = GameReflection.GetKeyboardMappings();
                string takeMap = kbMappings != null &&
                                 (int)InputMappingIcons.Actions.Take < kbMappings.Length
                    ? kbMappings[(int)InputMappingIcons.Actions.Take]
                    : InputMappingIcons.GetMappingFor(InputMappingIcons.Actions.Take);
                string buildMap = kbMappings != null &&
                                  (int)InputMappingIcons.Actions.Build < kbMappings.Length
                    ? kbMappings[(int)InputMappingIcons.Actions.Build]
                    : InputMappingIcons.GetMappingFor(InputMappingIcons.Actions.Build);

                DebugLogger.Info(string.Format(
                    "[Verification] LocalPlayer active | RigBonesResolved={0} | UI Prompt Take='{1}' | UI Prompt Build='{2}'",
                    bonesOk, takeMap, buildMap));
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("Verification check exception: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            try
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;

                if (_stateMachine != null && _stateMachine.IsDropkicking)
                {
                    _stateMachine.ForceCancelDropkick("Plugin unloading");
                }

                if (_inputRemapManager != null)
                {
                    _inputRemapManager.RestoreOriginalBindings();
                }

                if (_harmony != null)
                {
                    _harmony.UnpatchSelf();
                }

                if (_modConfig != null)
                {
                    _modConfig.Dispose();
                }

                DebugLogger.Info(PluginName + " unloaded and original keybindings restored.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error during OnDestroy cleanup: " + ex);
            }
        }
    }
}
