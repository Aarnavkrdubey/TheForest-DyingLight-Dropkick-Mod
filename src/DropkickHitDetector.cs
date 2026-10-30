using System;
using System.Collections.Generic;
using TheForest.Utils;
using UnityEngine;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Collision-based hit detection system for the airborne dropkick.
    /// Uses both feet transforms (char_LeftFoot, char_RightFoot) and a forward kick collision volume,
    /// performs continuous inter-frame sphere sweeps to prevent high-speed tunneling,
    /// verifies line-of-sight so attacks never pass through solid walls, prevents self-damage,
    /// and deduplicates enemy root IDs so each enemy is hit at most once per dropkick.
    /// </summary>
    public sealed class DropkickHitDetector
    {
        private readonly HashSet<int> _hitEnemyIds = new HashSet<int>();
        private Vector3 _prevLeftFootPos;
        private Vector3 _prevRightFootPos;
        private Vector3 _prevCenterProbePos;
        private bool _hasPrevPositions;

        public void BeginDropkick(playerScriptSetup setup)
        {
            _hitEnemyIds.Clear();
            _hasPrevPositions = false;
            CaptureFootPositions(setup, LocalPlayer.Transform != null ? LocalPlayer.Transform.forward : Vector3.forward,
                out _prevLeftFootPos, out _prevRightFootPos, out _prevCenterProbePos);
            _hasPrevPositions = true;
        }

        public int EvaluateHits(
            playerScriptSetup setup,
            Vector3 kickDirection,
            float momentumMultiplier,
            bool wasSprinting,
            EnemyCombatIntegration combatIntegration,
            DropkickCameraFeedback cameraFeedback)
        {
            if (LocalPlayer.Transform == null || combatIntegration == null)
                return 0;

            Vector3 currLeftFoot;
            Vector3 currRightFoot;
            Vector3 currCenterProbe;
            CaptureFootPositions(setup, kickDirection, out currLeftFoot, out currRightFoot, out currCenterProbe);

            List<Collider> candidateColliders = new List<Collider>(24);

            // 1. Overlap spheres around both feet and the forward two-legged kick capsule
            Vector3 hipOrigin = LocalPlayer.Transform.position + Vector3.up * 0.85f;
            Vector3 kickTip = hipOrigin + kickDirection * 1.95f;

            Collider[] capsuleHits = Physics.OverlapCapsule(hipOrigin, kickTip, 0.58f, ~0, QueryTriggerInteraction.Collide);
            if (capsuleHits != null)
            {
                for (int i = 0; i < capsuleHits.Length; i++)
                    candidateColliders.Add(capsuleHits[i]);
            }

            Collider[] leftFootHits = Physics.OverlapSphere(currLeftFoot, 0.42f, ~0, QueryTriggerInteraction.Collide);
            if (leftFootHits != null)
            {
                for (int i = 0; i < leftFootHits.Length; i++)
                    candidateColliders.Add(leftFootHits[i]);
            }

            Collider[] rightFootHits = Physics.OverlapSphere(currRightFoot, 0.42f, ~0, QueryTriggerInteraction.Collide);
            if (rightFootHits != null)
            {
                for (int i = 0; i < rightFootHits.Length; i++)
                    candidateColliders.Add(rightFootHits[i]);
            }

            // 2. Continuous anti-tunneling sphere sweeps from previous frame positions to current frame positions
            if (_hasPrevPositions)
            {
                SweepSegmentForColliders(_prevLeftFootPos, currLeftFoot, 0.38f, candidateColliders);
                SweepSegmentForColliders(_prevRightFootPos, currRightFoot, 0.38f, candidateColliders);
                SweepSegmentForColliders(_prevCenterProbePos, currCenterProbe, 0.52f, candidateColliders);
            }

            _prevLeftFootPos = currLeftFoot;
            _prevRightFootPos = currRightFoot;
            _prevCenterProbePos = currCenterProbe;
            _hasPrevPositions = true;

            int newEnemyHits = 0;
            Transform playerRoot = LocalPlayer.Transform.root;

            for (int i = 0; i < candidateColliders.Count; i++)
            {
                Collider col = candidateColliders[i];
                if (col == null)
                    continue;

                // Never hit the local or remote player
                if (col.transform.root == playerRoot)
                    continue;

                GameObject go = col.gameObject;
                if (go.CompareTag("Player") || go.CompareTag("PlayerNet"))
                    continue;

                // Resolve EnemyHealth and mutantHitReceiver
                mutantHitReceiver receiver;
                EnemyHealth enemyHealth = ResolveEnemyComponents(col, out receiver);
                if (enemyHealth == null)
                    continue;

                // Deduplicate per enemy instance so a single dropkick never hits the same enemy twice
                int enemyKey = enemyHealth.GetInstanceID();
                if (_hitEnemyIds.Contains(enemyKey))
                    continue;

                // Ignore already-dead enemies
                if (GameReflection.GetEnemyDeadBlock(enemyHealth) || enemyHealth.Health <= 0)
                    continue;

                // Check directional cone (must be in front of player, not behind)
                Vector3 enemyCenter = col.bounds.center;
                Vector3 toEnemy = enemyCenter - hipOrigin;
                toEnemy.y = 0f;
                if (toEnemy.sqrMagnitude > 0.04f && Vector3.Dot(toEnemy.normalized, kickDirection) < -0.15f)
                    continue;

                // Line-of-sight wall occlusion check: prevent hitting enemies through solid walls/structures
                if (IsOccludedBySolidWall(hipOrigin, enemyCenter))
                {
                    DebugLogger.Debug("Blocked dropkick hit on " + enemyHealth.name + " due to solid wall occlusion.");
                    continue;
                }

                // Register hit
                _hitEnemyIds.Add(enemyKey);

                bool isHeavyMutant;
                bool hitApplied = combatIntegration.ApplyDropkickHit(
                    col,
                    enemyHealth,
                    receiver,
                    kickDirection,
                    momentumMultiplier,
                    wasSprinting,
                    out isHeavyMutant);

                if (hitApplied)
                {
                    newEnemyHits++;
                    if (cameraFeedback != null)
                    {
                        cameraFeedback.OnEnemyHit(enemyCenter, momentumMultiplier, isHeavyMutant);
                    }
                }
            }

            return newEnemyHits;
        }

        private static void CaptureFootPositions(
            playerScriptSetup setup,
            Vector3 kickDirection,
            out Vector3 leftFootPos,
            out Vector3 rightFootPos,
            out Vector3 centerProbePos)
        {
            Vector3 fallbackBase = LocalPlayer.Transform != null
                ? LocalPlayer.Transform.position + Vector3.up * 0.9f + kickDirection * 1.35f
                : Vector3.zero;

            Transform lf = (DropkickAnimator.Instance != null && DropkickAnimator.Instance.LeftFoot != null)
                ? DropkickAnimator.Instance.LeftFoot
                : null;

            Transform rf = (DropkickAnimator.Instance != null && DropkickAnimator.Instance.RightFoot != null)
                ? DropkickAnimator.Instance.RightFoot
                : null;

            leftFootPos = lf != null ? lf.position : fallbackBase;
            rightFootPos = rf != null ? rf.position : fallbackBase;
            centerProbePos = fallbackBase;
        }

        private static void SweepSegmentForColliders(Vector3 start, Vector3 end, float radius, List<Collider> output)
        {
            Vector3 delta = end - start;
            float dist = delta.magnitude;
            if (dist < 0.02f)
                return;

            RaycastHit[] hits = Physics.SphereCastAll(start, radius, delta / dist, dist, ~0, QueryTriggerInteraction.Collide);
            if (hits == null)
                return;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider != null)
                {
                    output.Add(hits[i].collider);
                }
            }
        }

        private static EnemyHealth ResolveEnemyComponents(Collider col, out mutantHitReceiver receiver)
        {
            receiver = col.GetComponent<mutantHitReceiver>();
            if (receiver == null)
            {
                receiver = col.GetComponentInParent<mutantHitReceiver>();
            }

            if (receiver != null)
            {
                EnemyHealth recHealth = GameReflection.GetReceiverHealth(receiver);
                if (recHealth != null)
                {
                    return recHealth;
                }
            }

            EnemyHealth health = col.GetComponent<EnemyHealth>();
            if (health == null)
            {
                health = col.GetComponentInParent<EnemyHealth>();
            }

            if (health == null && (col.CompareTag("enemyCollide") || col.CompareTag("enemyHead") || col.CompareTag("EnemyBodyPart")))
            {
                Transform root = col.transform.root;
                if (root != null)
                {
                    health = root.GetComponentInChildren<EnemyHealth>();
                    if (receiver == null)
                    {
                        receiver = root.GetComponentInChildren<mutantHitReceiver>();
                    }
                }
            }

            return health;
        }

        private static bool IsOccludedBySolidWall(Vector3 origin, Vector3 target)
        {
            Vector3 dir = target - origin;
            float dist = dir.magnitude;
            if (dist < 0.25f)
                return false;

            int mask = LocalPlayer.FpCharacter != null ? (int)LocalPlayer.FpCharacter.CollisionLayers : ~0;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir / dist, dist * 0.92f, mask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            Transform playerRoot = LocalPlayer.Transform != null ? LocalPlayer.Transform.root : null;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.isTrigger)
                    continue;

                if (playerRoot != null && c.transform.root == playerRoot)
                    continue;

                GameObject go = c.gameObject;
                if (go.CompareTag("Player") || go.CompareTag("PlayerNet") ||
                    go.CompareTag("enemyCollide") || go.CompareTag("enemyHead") ||
                    go.CompareTag("EnemyBodyPart") || go.CompareTag("animalCollide"))
                {
                    continue;
                }

                if (c.GetComponentInParent<EnemyHealth>() != null || c.GetComponentInParent<mutantHitReceiver>() != null)
                {
                    continue;
                }

                // Hit a solid environment/structure/wall collider between the player and the enemy
                return true;
            }

            return false;
        }
    }
}
