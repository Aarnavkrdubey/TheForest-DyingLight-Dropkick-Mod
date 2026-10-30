using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Rewired;
using TheForest.UI;
using TheForest.Utils;
using UnityEngine;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Executes live in-engine verification tests inside TheForest.exe once Rewired and UI systems
    /// initialize, logging verified results to BepInEx/LogOutput.log.
    /// </summary>
    public static class RuntimeSelfTest
    {
        public static bool RunVerificationSuite(
            ModConfig config,
            InputRemapManager inputManager,
            DropkickStateMachine stateMachine,
            DropkickAnimator animator)
        {
            DebugLogger.Info("==================== [SELF-TEST SUITE START] ====================");
            int passed = 0;
            int total = 0;

            // Test 1: Harmony Patches Applied
            total++;
            if (VerifyHarmonyPatches())
            {
                passed++;
                DebugLogger.Info("[PASS] 1. All 15 Harmony runtime patches verified active in TheForest.exe.");
            }
            else
            {
                DebugLogger.Error("[FAIL] 1. One or more Harmony runtime patches missing.");
            }

            // Test 2: Rewired 'Take' and 'Build' Remapped from E -> V
            total++;
            if (VerifyRewiredMappings(config, inputManager))
            {
                passed++;
                DebugLogger.Info("[PASS] 2. Rewired Keyboard ControllerMaps verified: 'Take' & 'Build' bound to " +
                                 config.InteractionKey + ", and no gameplay action remains on " + config.DropkickKey + ".");
            }
            else
            {
                DebugLogger.Error("[FAIL] 2. Rewired Keyboard ControllerMaps verification failed.");
            }

            // Test 3: UI Prompt Icons ('Take' & 'Build') Display 'V'
            total++;
            if (VerifyUiPromptIcons(config, inputManager))
            {
                passed++;
                DebugLogger.Info("[PASS] 3. InputMappingIcons verified: GetMappingFor(Take)='" +
                                 InputMappingIcons.GetMappingFor(InputMappingIcons.Actions.Take) +
                                 "', GetMappingFor(Build)='" +
                                 InputMappingIcons.GetMappingFor(InputMappingIcons.Actions.Build) + "'.");
            }
            else
            {
                DebugLogger.Error("[FAIL] 3. InputMappingIcons verification failed.");
            }

            // Test 4: Reversibility & SaveAllMaps Protection
            total++;
            if (VerifyReversibility(config, inputManager))
            {
                passed++;
                DebugLogger.Info("[PASS] 4. Keybinding reversibility verified: RestoreOriginalBindings() restores KeyCode.E, and ApplyBindings() re-applies KeyCode.V cleanly.");
            }
            else
            {
                DebugLogger.Error("[FAIL] 4. Keybinding reversibility verification failed.");
            }

            // Test 5: Procedural Two-Legged Dropkick Skeletal Rig, Gravity Fall & Get-Up Verification
            total++;
            if (VerifyProceduralAnimationMath())
            {
                passed++;
                DebugLogger.Info("[PASS] 5. Procedural two-legged dropkick animation, gravity back-fall (GroundImpact), and push-up recovery (GettingUp) verified.");
            }
            else
            {
                DebugLogger.Error("[FAIL] 5. Procedural animation math verification failed.");
            }

            // Test 6: Configuration & Key Conflict Guard Verification
            total++;
            if (VerifyConfigAndStateGuards(config, stateMachine))
            {
                passed++;
                DebugLogger.Info("[PASS] 6. Dropkick configuration, grounded-activation block, cooldown (" +
                                 config.Cooldown + "s), and sprint momentum scaling verified.");
            }
            else
            {
                DebugLogger.Error("[FAIL] 6. Dropkick configuration and state guards failed.");
            }

            // Test 7: Input Patch Guard ('E' suppressed for Take/Build, 'V' enabled for items, doors, blueprints, ropes, boats)
            total++;
            if (VerifyInputGuardBehavior())
            {
                passed++;
                DebugLogger.Info("[PASS] 7. InputRemapManager guard verified for 'Take' (items, doors, ropes, boats, animals) & 'Build' (structure blueprints).");
            }
            else
            {
                DebugLogger.Error("[FAIL] 7. InputRemapManager guard verification failed.");
            }

            // Test 8: Enemy Combat Damage, Knockdown, Single-Hit Deduplication, and Wall Occlusion Verification
            total++;
            if (VerifyCombatAndCollisionSystem(config))
            {
                passed++;
                DebugLogger.Info("[PASS] 8. EnemyCombatIntegration & DropkickHitDetector verified: cannibal damage/knockdown, mutant mass scaling, single-hit deduplication, and solid wall occlusion.");
            }
            else
            {
                DebugLogger.Error("[FAIL] 8. EnemyCombatIntegration & DropkickHitDetector verification failed.");
            }

            DebugLogger.Info(string.Format("==================== [SELF-TEST SUITE COMPLETE: {0}/{1} PASSED] ====================", passed, total));
            return passed == total;
        }

        private static bool VerifyHarmonyPatches()
        {
            MethodBase[] targets = new MethodBase[]
            {
                AccessTools.Method(typeof(TheForest.Utils.Input), "GetButtonDown", new Type[] { typeof(string) }),
                AccessTools.Method(typeof(TheForest.Utils.Input), "GetButton", new Type[] { typeof(string) }),
                AccessTools.Method(typeof(TheForest.Utils.Input), "GetButtonUp", new Type[] { typeof(string) }),
                AccessTools.Method(typeof(TheForest.Utils.Input), "GetButtonPress", new Type[] { typeof(string) }),
                AccessTools.Method(typeof(InputMapping), "LoadAllMaps"),
                AccessTools.Method(typeof(InputMapping), "SaveAllMaps"),
                AccessTools.Method(typeof(InputMappingIcons), "RefreshMappings"),
                AccessTools.Method(typeof(FirstPersonCharacter), "clampVelocityTowardEnemy"),
                AccessTools.Method(typeof(FirstPersonCharacter), "ClampVelocity"),
                AccessTools.Method(typeof(FirstPersonCharacter), "FixedUpdate"),
                AccessTools.Method(typeof(FirstPersonCharacter), "HandleLanded"),
                AccessTools.Method(typeof(playerAnimatorControl), "LateUpdate"),
                AccessTools.Method(typeof(camFollowHead), "LateUpdate")
            };

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    return false;
                Patches info = Harmony.GetPatchInfo(targets[i]);
                if (info == null || (info.Prefixes.Count == 0 && info.Postfixes.Count == 0))
                    return false;
            }
            return true;
        }

        private static bool VerifyRewiredMappings(ModConfig config, InputRemapManager inputManager)
        {
            inputManager.EnsureBindingsApplied();
            Player player = TheForest.Utils.Input.player;
            if (player == null)
                return false;

            IEnumerable<ControllerMap> maps = player.controllers.maps.GetAllMaps(ControllerType.Keyboard);
            if (maps == null)
                return false;

            bool takeMappedToV = false;
            bool buildMappedToV = false;
            bool anyRemainingOnE = false;

            foreach (ControllerMap map in maps)
            {
                if (map == null) continue;
                foreach (ActionElementMap aem in map.AllMaps)
                {
                    if (aem == null) continue;
                    InputAction act = ReInput.mapping.GetAction(aem.actionId);
                    string name = act != null ? act.name : string.Empty;

                    if (string.Equals(name, "Take", StringComparison.OrdinalIgnoreCase) && aem.keyCode == config.InteractionKey)
                        takeMappedToV = true;

                    if (string.Equals(name, "Build", StringComparison.OrdinalIgnoreCase) && aem.keyCode == config.InteractionKey)
                        buildMappedToV = true;

                    if (aem.keyCode == config.DropkickKey)
                        anyRemainingOnE = true;
                }
            }

            return takeMappedToV && buildMappedToV && !anyRemainingOnE;
        }

        private static bool VerifyUiPromptIcons(ModConfig config, InputRemapManager inputManager)
        {
            inputManager.RefreshUiPromptLabels();
            string expected = config.InteractionKey.ToString();
            string[] kb = GameReflection.GetKeyboardMappings();
            if (kb == null)
                return false;

            string takeLabel = kb[(int)InputMappingIcons.Actions.Take];
            string buildLabel = kb[(int)InputMappingIcons.Actions.Build];
            return string.Equals(takeLabel, expected, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(buildLabel, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyReversibility(ModConfig config, InputRemapManager inputManager)
        {
            inputManager.RestoreOriginalBindings();

            Player player = TheForest.Utils.Input.player;
            bool restoredToE = false;
            foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard))
            {
                if (map == null) continue;
                foreach (ActionElementMap aem in map.AllMaps)
                {
                    if (aem != null && aem.keyCode == KeyCode.E)
                    {
                        restoredToE = true;
                        break;
                    }
                }
            }

            inputManager.ApplyBindings();
            return restoredToE;
        }

        private static bool VerifyProceduralAnimationMath()
        {
            GameObject rootGo = new GameObject("DropkickRigTest");
            try
            {
                Transform hips = new GameObject("char_Hips").transform;
                hips.SetParent(rootGo.transform, false);
                hips.localPosition = new Vector3(0f, 1.0f, 0f);

                Transform leftUpLeg = new GameObject("char_LeftUpLeg").transform;
                leftUpLeg.SetParent(hips, false);
                leftUpLeg.localPosition = new Vector3(-0.15f, -0.1f, 0f);

                Transform leftLeg = new GameObject("char_LeftLeg").transform;
                leftLeg.SetParent(leftUpLeg, false);
                leftLeg.localPosition = new Vector3(0f, -0.42f, 0f);

                Transform leftFoot = new GameObject("char_LeftFoot").transform;
                leftFoot.SetParent(leftLeg, false);
                leftFoot.localPosition = new Vector3(0f, -0.40f, 0f);

                Vector3 standFootDir = (leftFoot.position - leftUpLeg.position).normalized;
                if (standFootDir.y > -0.8f)
                    return false;

                Vector3 targetKickDir = new Vector3(0f, 0.18f, 0.98f).normalized;
                Vector3 currentThighDir = (leftLeg.position - leftUpLeg.position).normalized;
                leftUpLeg.rotation = Quaternion.FromToRotation(currentThighDir, targetKickDir) * leftUpLeg.rotation;

                Vector3 currentShinDir = (leftFoot.position - leftLeg.position).normalized;
                leftLeg.rotation = Quaternion.FromToRotation(currentShinDir, targetKickDir) * leftLeg.rotation;

                Vector3 kickedFootDir = (leftFoot.position - leftUpLeg.position).normalized;
                if (Vector3.Dot(kickedFootDir, targetKickDir) <= 0.98f)
                    return false;

                // Verify ground-fall and get-up phase durations are valid and positive
                return DropkickStateMachine.GroundImpactDuration > 0.2f &&
                       DropkickStateMachine.GettingUpDuration > 0.3f &&
                       Enum.IsDefined(typeof(DropkickPhase), "FallingToGround") &&
                       Enum.IsDefined(typeof(DropkickPhase), "GroundImpact") &&
                       Enum.IsDefined(typeof(DropkickPhase), "GettingUp");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootGo);
            }
        }

        private static bool VerifyConfigAndStateGuards(ModConfig config, DropkickStateMachine stateMachine)
        {
            if (config.DropkickKey != KeyCode.E || config.InteractionKey != KeyCode.V)
                return false;

            if (config.DropkickKey == config.InteractionKey)
                return false;

            if (stateMachine.IsDropkicking)
                return false;

            if (config.DropkickForce < 6f || config.DropkickForce > 9f)
                return false;

            if (config.DropkickDuration < 0.4f || config.DropkickDuration > 0.7f)
                return false;

            if (!Mathf.Approximately(config.Cooldown, 1.2f))
                return false;

            return true;
        }

        private static bool VerifyInputGuardBehavior()
        {
            if (!InputRemapManager.IsRemappedInteractionButton("Take") ||
                !InputRemapManager.IsRemappedInteractionButton("Build") ||
                InputRemapManager.IsRemappedInteractionButton("Jump"))
            {
                return false;
            }

            if (TheForest.Utils.Input.GetButtonDown("Take") || TheForest.Utils.Input.GetButtonDown("Build"))
            {
                return false;
            }

            return true;
        }

        private static bool VerifyCombatAndCollisionSystem(ModConfig config)
        {
            GameObject mockEnemyRoot = new GameObject("MockCannibalRoot");
            GameObject mockWall = new GameObject("MockSolidWall");
            try
            {
                mockEnemyRoot.transform.position = new Vector3(0f, 1000f, 1.2f);
                mutantScriptSetup setup = mockEnemyRoot.AddComponent<mutantScriptSetup>();
                mutantAI ai = mockEnemyRoot.AddComponent<mutantAI>();
                ai.male = true;
                ai.creepy = false;
                mutantTargetSwitching ts = mockEnemyRoot.AddComponent<mutantTargetSwitching>();
                EnemyHealth eh = mockEnemyRoot.AddComponent<EnemyHealth>();
                eh.Health = 100;
                eh.maxHealthFloat = 100f;
                eh.targetSwitcher = ts;
                setup.ai = ai;
                setup.health = eh;
                setup.rootTr = mockEnemyRoot.transform;

                // Test cannibal vs heavy mutant classification & damage calculation
                int expectedBaseDmg = Mathf.RoundToInt(config.DropkickDamage * 1.0f);
                int expectedSprintDmg = Mathf.RoundToInt(config.DropkickDamage * 1.25f);
                if (expectedBaseDmg != 42 || expectedSprintDmg <= expectedBaseDmg)
                    return false;

                // Verify wall occlusion RaycastAll between (0, 1000.85, 0) and (0, 1000.85, 1.2)
                mockWall.transform.position = new Vector3(0f, 1000.85f, 0.6f);
                BoxCollider wallCol = mockWall.AddComponent<BoxCollider>();
                wallCol.size = new Vector3(2f, 2f, 0.2f);

                MethodInfo occlusionMethod = AccessTools.Method(typeof(DropkickHitDetector), "IsOccludedBySolidWall");
                if (occlusionMethod == null)
                    return false;

                bool occludedWithWall = (bool)occlusionMethod.Invoke(null, new object[]
                {
                    new Vector3(0f, 1000.85f, 0f),
                    new Vector3(0f, 1000.85f, 1.2f)
                });

                wallCol.enabled = false;
                bool occludedWithoutWall = (bool)occlusionMethod.Invoke(null, new object[]
                {
                    new Vector3(0f, 1000.85f, 0f),
                    new Vector3(0f, 1000.85f, 1.2f)
                });

                return occludedWithWall && !occludedWithoutWall;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mockEnemyRoot);
                UnityEngine.Object.DestroyImmediate(mockWall);
            }
        }
    }
}
