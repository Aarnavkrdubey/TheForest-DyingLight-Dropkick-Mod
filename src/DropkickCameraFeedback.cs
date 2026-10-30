using System;
using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Provides first-person camera movement across the entire Dying Light dropkick sequence:
    /// midair kick framing, impact camera shake, gravity fall tilt, heavy back-fall ground landing thud,
    /// hand-plant push-up recovery motion, and authentic FMOD audio feedback.
    /// </summary>
    public sealed class DropkickCameraFeedback
    {
        public static DropkickCameraFeedback Instance { get; private set; }

        private readonly ModConfig _config;
        private readonly DropkickStateMachine _stateMachine;

        private float _shakeRemainingTime;
        private float _shakeDuration;
        private float _shakeMagnitude;
        private float _shakeSeed;

        private float _baseFov = -1f;
        private float _currentFovOffset;

        private string _cachedFleshHitEvent;
        private string _cachedSmashHitEvent;
        private bool _audioEventsResolved;

        public DropkickCameraFeedback(ModConfig config, DropkickStateMachine stateMachine)
        {
            _config = config;
            _stateMachine = stateMachine;
            Instance = this;
        }

        public void OnDropkickStart(bool isSprinting)
        {
            try
            {
                if (LocalPlayer.Sfx != null)
                {
                    LocalPlayer.Sfx.PlayJumpSound();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("OnDropkickStart audio exception: " + ex.Message);
            }
        }

        public void OnDropkickExtension()
        {
            try
            {
                if (LocalPlayer.Sfx != null)
                {
                    LocalPlayer.Sfx.PlayWhoosh();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("OnDropkickExtension audio exception: " + ex.Message);
            }
        }

        public void OnEnemyHit(Vector3 hitPosition, float momentumMultiplier, bool isHeavyMutant)
        {
            // 1. Trigger impact camera shake according to config
            TriggerCameraShake(momentumMultiplier * 1.15f, 0.24f);

            // 2. Play authentic game flesh/smash impact audio & controller rumble
            try
            {
                ResolveWeaponAudioEventsIfNeeded();

                Transform emitTr = LocalPlayer.Transform != null ? LocalPlayer.Transform : null;

                if (!string.IsNullOrEmpty(_cachedFleshHitEvent) && emitTr != null)
                {
                    FMODCommon.PlayOneshotNetworked(_cachedFleshHitEvent, emitTr, FMODCommon.NetworkRole.Any);
                }

                if (!string.IsNullOrEmpty(_cachedSmashHitEvent))
                {
                    FMODCommon.PlayOneshot(_cachedSmashHitEvent, hitPosition, new object[0]);
                }

                if (LocalPlayer.ScriptSetup != null && LocalPlayer.ScriptSetup.pmNoise != null)
                {
                    LocalPlayer.ScriptSetup.pmNoise.SendEvent("toWeaponNoise");
                }

                Mood.HitRumble();
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("OnEnemyHit audio exception: " + ex.Message);
            }
        }

        public void OnGroundFallImpact(bool hadHitEnemy, float momentumMultiplier)
        {
            // Heavy ground back-fall thud shake + rumble + landing audio
            float fallShakeIntensity = (hadHitEnemy ? 1.05f : 0.85f) * Mathf.Clamp(momentumMultiplier, 1f, 1.35f);
            TriggerCameraShake(fallShakeIntensity, 0.26f);

            try
            {
                if (LocalPlayer.Sfx != null)
                {
                    LocalPlayer.Sfx.PlayJumpSound();
                    LocalPlayer.Sfx.PlayPlantRustle();
                }

                Mood.HitRumble();
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("OnGroundFallImpact audio exception: " + ex.Message);
            }
        }

        public void OnStartGettingUp()
        {
            try
            {
                if (LocalPlayer.Sfx != null)
                {
                    LocalPlayer.Sfx.PlayPlantRustle();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("OnStartGettingUp audio exception: " + ex.Message);
            }
        }

        private void ResolveWeaponAudioEventsIfNeeded()
        {
            if (_audioEventsResolved && !string.IsNullOrEmpty(_cachedFleshHitEvent))
                return;

            if (LocalPlayer.GameObject != null)
            {
                weaponInfo[] weapons = LocalPlayer.GameObject.GetComponentsInChildren<weaponInfo>(true);
                if (weapons != null)
                {
                    for (int i = 0; i < weapons.Length; i++)
                    {
                        weaponInfo w = weapons[i];
                        if (w == null) continue;

                        if (string.IsNullOrEmpty(_cachedFleshHitEvent) && !string.IsNullOrEmpty(w.fleshHitEvent))
                        {
                            _cachedFleshHitEvent = w.fleshHitEvent;
                        }
                        if (string.IsNullOrEmpty(_cachedSmashHitEvent) && !string.IsNullOrEmpty(w.smashHitEvent))
                        {
                            _cachedSmashHitEvent = w.smashHitEvent;
                        }

                        if (!string.IsNullOrEmpty(_cachedFleshHitEvent) && !string.IsNullOrEmpty(_cachedSmashHitEvent))
                            break;
                    }
                }
            }

            if (string.IsNullOrEmpty(_cachedFleshHitEvent))
            {
                _cachedFleshHitEvent = "event:/combat/flesh_hit";
            }
            if (string.IsNullOrEmpty(_cachedSmashHitEvent))
            {
                _cachedSmashHitEvent = "event:/combat/smash_hit";
            }

            _audioEventsResolved = true;
        }

        private void TriggerCameraShake(float strengthMultiplier, float duration)
        {
            CameraShakeIntensity intensity = _config.CameraShake;
            if (intensity == CameraShakeIntensity.Off)
                return;

            float scale = 1.0f;
            switch (intensity)
            {
                case CameraShakeIntensity.Low:
                    scale = 1.0f;
                    break;
                case CameraShakeIntensity.Medium:
                    scale = 1.65f;
                    break;
                case CameraShakeIntensity.High:
                    scale = 2.4f;
                    break;
            }

            _shakeDuration = duration;
            _shakeRemainingTime = _shakeDuration;
            _shakeMagnitude = 1.75f * scale * Mathf.Clamp(strengthMultiplier, 0.5f, 1.8f);
            _shakeSeed = UnityEngine.Random.Range(0f, 100f);
        }

        /// <summary>
        /// Runs in LateUpdate after camFollowHead.LateUpdate to apply camera position,
        /// pitch/roll across the kick, fall, ground impact, and push-up recovery, plus impact shake.
        /// </summary>
        public void ApplyCameraLateUpdate(camFollowHead camFollow)
        {
            if (LocalPlayer.MainCam == null || LocalPlayer.MainCamTr == null)
                return;

            // Ensure skeletal pose has been applied for this frame
            if (DropkickAnimator.Instance != null)
            {
                DropkickAnimator.Instance.ApplyPoseLateUpdate();
            }

            bool isKicking = _stateMachine != null && _stateMachine.IsDropkicking;
            float dt = Time.deltaTime;

            if (isKicking)
            {
                // 1. Anchor camera world position to DesiredCameraWorldPos so that:
                //    - During Startup/ActiveKick: camera sits cleanly behind/above Hips looking down both boots
                //    - During FallingToGround: camera drops with the horizontal body
                //    - During GroundImpact: camera drops down close to the dirt (-0.68m) on the player's back
                //    - During GettingUp: camera rises smoothly back to standing eye height
                if (camFollow != null && DropkickAnimator.Instance != null && DropkickAnimator.Instance.HasDesiredCameraWorldPos)
                {
                    camFollow.transform.position = DropkickAnimator.Instance.DesiredCameraWorldPos;
                }

                // 2. Compute phase-specific camera pitch, roll, and FOV offset
                float pitchOffset = 0f;
                float rollOffset = 0f;
                float targetFovOffset = 0f;

                switch (_stateMachine.CurrentPhase)
                {
                    case DropkickPhase.Startup:
                    {
                        float p = Mathf.Clamp01(_stateMachine.NormalizedTime / DropkickStateMachine.StartupEndNormalized);
                        float s = Mathf.SmoothStep(0f, 1f, p);
                        // Tilt camera slightly down (+10.5 deg) to frame knees & boots snapping forward
                        pitchOffset = 10.5f * s;
                        rollOffset = -3.8f * s;
                        targetFovOffset = 2.5f * s;
                        break;
                    }

                    case DropkickPhase.ActiveKick:
                    {
                        pitchOffset = 9.5f;
                        rollOffset = -3.2f;
                        targetFovOffset = 5.2f * _stateMachine.MomentumMultiplier;

                        if (_stateMachine.HasImpactedEnemy)
                        {
                            float hitT = Mathf.Clamp01((_stateMachine.TotalElapsed - _stateMachine.EnemyImpactTime) /
                                                       DropkickStateMachine.ImpactFollowThroughDuration);
                            pitchOffset = Mathf.Lerp(9.5f, 2.0f, hitT);
                            rollOffset = Mathf.Lerp(-3.2f, -8.5f, hitT);
                        }
                        break;
                    }

                    case DropkickPhase.FallingToGround:
                    {
                        float fallT = Mathf.Clamp01(_stateMachine.PhaseElapsed / 0.35f);
                        float s = Mathf.SmoothStep(0f, 1f, fallT);
                        // As gravity pulls the horizontal player toward the ground, tilt back & roll onto side
                        pitchOffset = Mathf.Lerp(4.0f, -6.5f, s);
                        rollOffset = Mathf.Lerp(-6.0f, -12.0f, s);
                        targetFovOffset = Mathf.Lerp(3.0f, -1.5f, s);
                        break;
                    }

                    case DropkickPhase.GroundImpact:
                    {
                        float p = _stateMachine.GroundImpactNormalizedTime;
                        float impactJolt = Mathf.Sin(p * Mathf.PI * 2.5f) * Mathf.Exp(-p * 3.5f);
                        // Heavy back-fall on the dirt: camera looks slightly up toward boots/enemy and rolls onto side
                        pitchOffset = -9.5f + impactJolt * 7.5f;
                        rollOffset = -14.5f - impactJolt * 4.5f;
                        targetFovOffset = -3.0f * (1f - p * 0.5f);
                        break;
                    }

                    case DropkickPhase.GettingUp:
                    {
                        float p = _stateMachine.GettingUpNormalizedTime;
                        if (p < 0.45f)
                        {
                            // Roll from back/left (-14.5 deg roll) onto right hand (+11.5 deg roll) and look down (+14 deg pitch) as hands push off the dirt
                            float sub = Mathf.SmoothStep(0f, 1f, p / 0.45f);
                            pitchOffset = Mathf.Lerp(-9.5f, 14.0f, sub);
                            rollOffset = Mathf.Lerp(-14.5f, 11.5f, sub);
                        }
                        else
                        {
                            // Push up from hands/knees to standing: pitch and roll settle smoothly back to 0 deg
                            float sub = Mathf.SmoothStep(0f, 1f, (p - 0.45f) / 0.55f);
                            pitchOffset = Mathf.Lerp(14.0f, 0f, sub);
                            rollOffset = Mathf.Lerp(11.5f, 0f, sub);
                        }
                        targetFovOffset = 0f;
                        break;
                    }
                }

                // Apply pitch & roll cleanly to camFollow.transform.localRotation (which camFollowHead resets to identity each frame)
                Quaternion kickCamRot = Quaternion.Euler(pitchOffset, 0f, rollOffset);
                if (camFollow != null)
                {
                    camFollow.transform.localRotation = camFollow.transform.localRotation * kickCamRot;
                }
                else
                {
                    LocalPlayer.MainCamTr.localRotation = LocalPlayer.MainCamTr.localRotation * kickCamRot;
                }

                // Apply FOV speed/impact offset
                if (_baseFov < 0f)
                {
                    _baseFov = LocalPlayer.MainCam.fieldOfView;
                }
                _currentFovOffset = Mathf.Lerp(_currentFovOffset, targetFovOffset, dt * 16f);
                LocalPlayer.MainCam.fieldOfView = _baseFov + _currentFovOffset;
            }
            else if (_baseFov > 0f)
            {
                if (Mathf.Abs(_currentFovOffset) > 0.05f)
                {
                    _currentFovOffset = Mathf.Lerp(_currentFovOffset, 0f, dt * 12f);
                    LocalPlayer.MainCam.fieldOfView = _baseFov + _currentFovOffset;
                }
                else
                {
                    LocalPlayer.MainCam.fieldOfView = _baseFov;
                    _currentFovOffset = 0f;
                    _baseFov = -1f;
                }
            }

            // 3. Damped impact camera shake (on enemy hit or ground back-fall)
            if (_shakeRemainingTime > 0f)
            {
                _shakeRemainingTime -= dt;
                float damper = Mathf.Clamp01(_shakeRemainingTime / Mathf.Max(0.01f, _shakeDuration));
                float t = (Time.time + _shakeSeed) * 42f;

                float shakePitch = Mathf.Sin(t) * _shakeMagnitude * damper;
                float shakeYaw = Mathf.Cos(t * 1.17f) * (_shakeMagnitude * 0.65f) * damper;
                float shakeRoll = Mathf.Sin(t * 0.83f) * (_shakeMagnitude * 0.8f) * damper;

                Quaternion shakeRot = Quaternion.Euler(shakePitch, shakeYaw, shakeRoll);
                if (camFollow != null)
                {
                    camFollow.transform.localRotation = camFollow.transform.localRotation * shakeRot;
                }
                else
                {
                    LocalPlayer.MainCamTr.localRotation = LocalPlayer.MainCamTr.localRotation * shakeRot;
                }
            }
        }

        // =====================================================================
        // Harmony Postfix on camFollowHead.LateUpdate so camera feedback runs
        // cleanly after The Forest updates its head-tracking camera.
        // =====================================================================

        [HarmonyPatch(typeof(camFollowHead), "LateUpdate")]
        public static class Patch_CamFollowHead_LateUpdate
        {
            public static void Postfix(camFollowHead __instance)
            {
                if (Instance != null)
                {
                    Instance.ApplyCameraLateUpdate(__instance);
                }
            }
        }
    }
}
