using System;
using System.Collections;
using TheForest.Utils;
using UnityEngine;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Integrates dropkick hits with The Forest's actual enemy health, hit reaction, knockdown/stagger,
    /// physical knockback, and Bolt multiplayer synchronization systems.
    /// </summary>
    public sealed class EnemyCombatIntegration
    {
        private readonly ModConfig _config;
        private readonly MonoBehaviour _coroutineRunner;

        public EnemyCombatIntegration(ModConfig config, MonoBehaviour coroutineRunner)
        {
            _config = config;
            _coroutineRunner = coroutineRunner;
        }

        public bool ApplyDropkickHit(
            Collider hitCollider,
            EnemyHealth enemyHealth,
            mutantHitReceiver receiver,
            Vector3 kickDirection,
            float momentumMultiplier,
            bool wasSprinting,
            out bool isHeavyMutant)
        {
            isHeavyMutant = false;
            if (enemyHealth == null)
                return false;

            try
            {
                mutantScriptSetup setup = GameReflection.GetEnemySetup(enemyHealth);
                mutantAI ai = GameReflection.GetEnemyAI(enemyHealth);
                mutantTargetSwitching targetSwitcher = enemyHealth.targetSwitcher;

                // Determine if enemy is a heavy creepy mutant vs a normal cannibal
                bool isCreepyBaby = ai != null && ai.creepy_baby;
                bool isCreepyBoss = ai != null && ai.creepy_boss;
                isHeavyMutant = (ai != null && (ai.creepy || ai.creepy_male || ai.creepy_fat || ai.creepy_boss)) ||
                                (targetSwitcher != null && (targetSwitcher.typeMaleCreepy || targetSwitcher.typeFemaleCreepy || targetSwitcher.typeFatCreepy));

                // Ignore un-transformed endgame boss
                if (isCreepyBoss && ai != null && !ai.girlFullyTransformed)
                    return false;

                // Calculate configurable physical damage scaled by sprint momentum
                int finalDamage = Mathf.Max(1, Mathf.RoundToInt(_config.DropkickDamage * momentumMultiplier));

                // Determine relative attack direction (0 = front, 1 = behind)
                int takeDamageDir = ComputeRelativeHitDirection(hitCollider, enemyHealth);

                // Notify enemy AI of player attacker
                if (targetSwitcher != null)
                {
                    targetSwitcher.attackerType = 4;
                }

                if (hitCollider != null && hitCollider.gameObject != null)
                {
                    hitCollider.gameObject.SendMessage("getAttackerType", 4, SendMessageOptions.DontRequireReceiver);
                    if (LocalPlayer.GameObject != null)
                    {
                        hitCollider.gameObject.SendMessage("getAttacker", LocalPlayer.GameObject, SendMessageOptions.DontRequireReceiver);
                    }
                }

                if (receiver != null && LocalPlayer.Transform != null)
                {
                    GameReflection.InvokeGetSkinHitPosition(receiver, LocalPlayer.Transform);
                    receiver.takeDamage(takeDamageDir);
                }
                else
                {
                    enemyHealth.takeDamage(takeDamageDir);
                }

                // Trigger blood spatter on player weapon/hands
                if (LocalPlayer.GameObject != null)
                {
                    LocalPlayer.GameObject.SendMessage("GotBloody", SendMessageOptions.DontRequireReceiver);
                }

                // Handle Multiplayer Client vs Singleplayer / Host Server
                bool isMpClient = BoltNetwork.isRunning && BoltNetwork.isClient;
                if (isMpClient)
                {
                    SendMultiplayerHit(hitCollider, enemyHealth, finalDamage, takeDamageDir, !isHeavyMutant);
                }
                else
                {
                    if (!isHeavyMutant && !isCreepyBaby)
                    {
                        ApplySingleplayerCannibalHit(enemyHealth, finalDamage, takeDamageDir);
                    }
                    else
                    {
                        ApplySingleplayerMutantHit(enemyHealth, setup, ai, finalDamage, wasSprinting);
                    }
                }

                // Apply physical knockback displacement scaled by momentum and enemy mass
                if (_coroutineRunner != null)
                {
                    float massMultiplier = isHeavyMutant ? _config.HeavyMutantKnockbackMultiplier : 1.0f;
                    if (isCreepyBoss)
                    {
                        massMultiplier *= 0.5f;
                    }

                    float initialPushSpeed = _config.EnemyKnockbackForce * momentumMultiplier * massMultiplier;
                    float pushDuration = isHeavyMutant ? 0.26f : 0.38f;

                    _coroutineRunner.StartCoroutine(
                        KnockbackEnemyCoroutine(enemyHealth, setup, kickDirection, initialPushSpeed, pushDuration));
                }

                DebugLogger.Info(string.Format(
                    "Dropkick hit '{0}' | HeavyMutant={1} | Damage={2} | Momentum={3:F2}x | RemainingHP={4}",
                    enemyHealth.name, isHeavyMutant, finalDamage, momentumMultiplier, enemyHealth.Health));

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("Error in ApplyDropkickHit: " + ex);
                return false;
            }
        }

        private static int ComputeRelativeHitDirection(Collider hitCollider, EnemyHealth enemyHealth)
        {
            if (LocalPlayer.Transform == null)
                return 0;

            mutantScriptSetup setup = GameReflection.GetEnemySetup(enemyHealth);
            Transform refTr = (setup != null && setup.rootTr != null)
                ? setup.rootTr
                : enemyHealth.transform.root;

            if (refTr == null)
                return 0;

            Vector3 localPos = refTr.InverseTransformPoint(LocalPlayer.Transform.position);
            float angle = Mathf.Atan2(localPos.x, localPos.z) * Mathf.Rad2Deg;
            return (angle < -140f || angle > 140f) ? 1 : 0;
        }

        private static void ApplySingleplayerCannibalHit(EnemyHealth enemyHealth, int finalDamage, int takeDamageDir)
        {
            enemyHealth.hitBlock = false;
            if (enemyHealth.targetSwitcher != null)
            {
                enemyHealth.targetSwitcher.attackerType = 4;
            }

            enemyHealth.takeDamage(takeDamageDir);
            enemyHealth.getAttackDirection(5);
            enemyHealth.getCombo(3);

            // If this dropkick is lethal, route through HitReal so death state, blood, and ragdoll run cleanly
            if (enemyHealth.Health - finalDamage < 1)
            {
                enemyHealth.HitReal(finalDamage);
                return;
            }

            // Otherwise, EnemyHealth.hitFallDown(float) subtracts a hardcoded 20 HP and triggers knockdown
            // (hurtLevelInt = 4, damageTrigger, pmCombat "gotHit"). We pre-adjust Health by (finalDamage - 20)
            // so the exact configured damage is applied along with the full knockdown animation!
            int deltaFromTwenty = finalDamage - 20;
            enemyHealth.Health -= deltaFromTwenty;

            enemyHealth.hitFallDown(finalDamage);
            GameReflection.InvokeEnemyBlood(enemyHealth);
            enemyHealth.setSkinDamage(UnityEngine.Random.Range(0, 3));

            if (!string.IsNullOrEmpty(enemyHealth.HurtEvent))
            {
                float healthPct = enemyHealth.maxHealthFloat > 0f
                    ? Mathf.Clamp01((float)enemyHealth.Health / enemyHealth.maxHealthFloat)
                    : 0.5f;
                FMODCommon.PlayOneshot(
                    enemyHealth.HurtEvent,
                    enemyHealth.transform.position,
                    new object[] { "mutant_health", healthPct });
            }
        }

        private static void ApplySingleplayerMutantHit(
            EnemyHealth enemyHealth,
            mutantScriptSetup setup,
            mutantAI ai,
            int finalDamage,
            bool wasSprinting)
        {
            enemyHealth.hitBlock = false;
            if (enemyHealth.targetSwitcher != null)
            {
                enemyHealth.targetSwitcher.attackerType = 4;
            }

            enemyHealth.getAttackDirection(3);
            enemyHealth.getCombo(3);
            enemyHealth.setSkinDamage(UnityEngine.Random.Range(0, 3));

            // Apply damage via HitReal
            enemyHealth.HitReal(finalDamage);

            // Trigger heavy stagger reaction on creepy mutants if still alive
            Animator enemyAnim = GameReflection.GetEnemyAnimator(enemyHealth);
            if (enemyHealth.Health >= 1 && enemyAnim != null)
            {
                bool isBoss = ai != null && ai.creepy_boss;
                if (!isBoss && !enemyAnim.GetBool("hitStagger"))
                {
                    if (setup != null && setup.pmCombat != null && setup.pmCombat.FsmVariables != null)
                    {
                        var staggeredBool = setup.pmCombat.FsmVariables.GetFsmBool("staggered");
                        if (staggeredBool != null)
                        {
                            staggeredBool.Value = true;
                        }
                        setup.pmCombat.SendEvent("toHitExplode");
                    }

                    enemyAnim.SetBool("hitStagger", true);
                    enemyHealth.CancelInvoke("resetStagger");
                    enemyHealth.Invoke("resetStagger", wasSprinting ? 4.0f : 2.5f);
                }
            }
        }

        private static void SendMultiplayerHit(
            Collider hitCollider,
            EnemyHealth enemyHealth,
            int finalDamage,
            int takeDamageDir,
            bool triggerKnockdown)
        {
            try
            {
                Transform root = hitCollider != null ? hitCollider.transform.root : enemyHealth.transform.root;
                if (root != null)
                {
                    CoopMutantClientHitPrediction pred = root.GetComponentInChildren<CoopMutantClientHitPrediction>();
                    if (pred != null)
                    {
                        pred.getClientHitDirection(triggerKnockdown ? 5 : 3);
                        pred.StartPrediction();
                    }
                }

                BoltEntity targetEntity = null;
                if (hitCollider != null)
                {
                    targetEntity = hitCollider.GetComponentInParent<BoltEntity>();
                }
                if (targetEntity == null && enemyHealth != null)
                {
                    targetEntity = enemyHealth.GetComponentInParent<BoltEntity>();
                }
                if (targetEntity == null && root != null)
                {
                    targetEntity = root.GetComponent<BoltEntity>() ?? root.GetComponentInChildren<BoltEntity>();
                }

                if (targetEntity == null)
                    return;

                PlayerHitEnemy ev = PlayerHitEnemy.Create(Bolt.GlobalTargets.OnlyServer);
                ev.Target = targetEntity;
                ev.getAttackerType = 4;
                ev.takeDamage = takeDamageDir;
                ev.getAttackDirection = triggerKnockdown ? 5 : 3;
                ev.getCombo = 3;
                ev.Hit = finalDamage;
                ev.hitFallDown = triggerKnockdown;
                ev.Send();
            }
            catch (Exception ex)
            {
                DebugLogger.Debug("Multiplayer SendMultiplayerHit exception: " + ex.Message);
            }
        }

        /// <summary>
        /// Smoothly pushes the hit enemy backward along the ground using their CharacterController
        /// (or obstacle-checked root transform if CharacterController is disabled), respecting solid walls
        /// and leaving zero permanent changes on enemy physics or AI.
        /// </summary>
        private static IEnumerator KnockbackEnemyCoroutine(
            EnemyHealth enemyHealth,
            mutantScriptSetup setup,
            Vector3 pushDir,
            float initialSpeed,
            float duration)
        {
            if (enemyHealth == null || initialSpeed <= 0.1f || duration <= 0.01f)
                yield break;

            pushDir.y = 0f;
            if (pushDir.sqrMagnitude < 0.001f)
                yield break;
            pushDir.Normalize();

            Transform rootTr = (setup != null && setup.rootTr != null)
                ? setup.rootTr
                : enemyHealth.transform.root;

            CharacterController cc = (setup != null && setup.controller != null)
                ? setup.controller
                : (rootTr != null ? (rootTr.GetComponent<CharacterController>() ?? rootTr.GetComponentInChildren<CharacterController>()) : null);

            int envMask = LocalPlayer.FpCharacter != null ? (int)LocalPlayer.FpCharacter.CollisionLayers : ~0;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (enemyHealth == null || rootTr == null)
                    yield break;

                float dt = Time.deltaTime;
                elapsed += dt;

                float t = Mathf.Clamp01(elapsed / duration);
                // Quadratic ease-out impulse curve
                float currentSpeed = initialSpeed * (1f - t) * (1f - t);
                if (currentSpeed < 0.15f)
                    break;

                Vector3 step = pushDir * (currentSpeed * dt);

                // Stop knockback if a solid wall/structure/tree is directly behind the enemy
                Vector3 checkOrigin = rootTr.position + Vector3.up * 0.9f;
                RaycastHit wallHit;
                if (Physics.SphereCast(checkOrigin, 0.35f, pushDir, out wallHit, step.magnitude + 0.45f, envMask, QueryTriggerInteraction.Ignore))
                {
                    if (wallHit.collider != null &&
                        wallHit.collider.transform.root != rootTr &&
                        wallHit.normal.y < 0.55f)
                    {
                        break;
                    }
                }

                if (cc != null && cc.enabled && cc.gameObject.activeInHierarchy)
                {
                    cc.Move(step + Vector3.down * (3.5f * dt));
                }
                else
                {
                    Vector3 nextPos = rootTr.position + step;
                    RaycastHit groundHit;
                    if (Physics.Raycast(nextPos + Vector3.up * 1.8f, Vector3.down, out groundHit, 4.0f, envMask, QueryTriggerInteraction.Ignore))
                    {
                        nextPos.y = groundHit.point.y;
                    }
                    rootTr.position = nextPos;
                }

                yield return null;
            }
        }
    }
}
