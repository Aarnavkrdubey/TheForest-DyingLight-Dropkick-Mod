using System;
using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Procedural skeletal animation controller for the Dying Light-style two-legged dropkick,
    /// gravity fall, back-fall ground impact, and hand-plant push-up recovery.
    /// Operates on the verified The Forest player rig bones (char_Hips, char_Spine*, char_LeftUpLeg,
    /// char_LeftLeg, char_LeftFoot, char_RightUpLeg, char_RightLeg, char_RightFoot, char_LeftArm, char_RightArm)
    /// in LateUpdate after the Animator evaluates.
    /// </summary>
    public sealed class DropkickAnimator
    {
        public static DropkickAnimator Instance { get; private set; }

        private readonly DropkickStateMachine _stateMachine;

        // Cached player rig transforms verified from playerScriptSetup & resources.assets
        private Transform _cachedRoot;
        public Transform Hips { get; private set; }
        public Transform Spine1 { get; private set; }
        public Transform Spine2 { get; private set; }
        public Transform Spine3 { get; private set; }
        public Transform Neck { get; private set; }
        public Transform Head { get; private set; }

        public Transform LeftUpLeg { get; private set; }
        public Transform LeftLeg { get; private set; }
        public Transform LeftFoot { get; private set; }
        public Transform LeftToe { get; private set; }

        public Transform RightUpLeg { get; private set; }
        public Transform RightLeg { get; private set; }
        public Transform RightFoot { get; private set; }
        public Transform RightToe { get; private set; }

        public Transform LeftArm { get; private set; }
        public Transform LeftForeArm { get; private set; }
        public Transform RightArm { get; private set; }
        public Transform RightForeArm { get; private set; }

        private bool _bonesResolved;
        private int _snapshotFrame = -1;

        // Clean per-frame Animator baseline snapshot so multiple LateUpdate hooks in the same frame
        // never compound rotations or get overwritten by playerAnimatorControl.LateUpdate
        private Vector3 _baseHipsPos;
        private Quaternion _baseHipsRot;
        private Quaternion _baseSpine1Rot;
        private Quaternion _baseSpine2Rot;
        private Quaternion _baseSpine3Rot;
        private Quaternion _baseNeckRot;
        private Quaternion _baseHeadRot;
        private Quaternion _baseLeftUpLegRot;
        private Quaternion _baseLeftLegRot;
        private Quaternion _baseLeftFootRot;
        private Quaternion _baseRightUpLegRot;
        private Quaternion _baseRightLegRot;
        private Quaternion _baseRightFootRot;
        private Quaternion _baseLeftArmRot;
        private Quaternion _baseLeftForeArmRot;
        private Quaternion _baseRightArmRot;
        private Quaternion _baseRightForeArmRot;

        // Computed target camera anchor for DropkickCameraFeedback
        public Vector3 DesiredCameraWorldPos { get; private set; }
        public bool HasDesiredCameraWorldPos { get; private set; }

        public DropkickAnimator(DropkickStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
            Instance = this;
        }

        public bool EnsureBonesResolved()
        {
            if (LocalPlayer.Transform == null || LocalPlayer.ScriptSetup == null)
            {
                _bonesResolved = false;
                return false;
            }

            if (_bonesResolved && _cachedRoot == LocalPlayer.Transform && LeftFoot != null && RightFoot != null && Hips != null)
            {
                return true;
            }

            _cachedRoot = LocalPlayer.Transform;
            playerScriptSetup setup = LocalPlayer.ScriptSetup;

            Hips = setup.hipsJnt != null ? setup.hipsJnt : FindBoneRecursive(_cachedRoot, "char_Hips");
            Spine1 = setup.spine1 != null ? setup.spine1 : FindBoneRecursive(_cachedRoot, "char_Spine");
            Spine2 = setup.spine2 != null ? setup.spine2 : FindBoneRecursive(_cachedRoot, "char_Spine1");
            Spine3 = setup.spine3 != null ? setup.spine3 : FindBoneRecursive(_cachedRoot, "char_Spine2");
            Neck = setup.neckJnt != null ? setup.neckJnt : FindBoneRecursive(_cachedRoot, "char_Neck");
            Head = setup.headJnt != null ? setup.headJnt : FindBoneRecursive(_cachedRoot, "char_Head1");

            LeftArm = setup.leftArm != null ? setup.leftArm : FindBoneRecursive(_cachedRoot, "char_LeftArm");
            LeftForeArm = FindBoneRecursive(_cachedRoot, "char_LeftForeArm");
            RightArm = setup.rightArm != null ? setup.rightArm : FindBoneRecursive(_cachedRoot, "char_RightArm");
            RightForeArm = FindBoneRecursive(_cachedRoot, "char_RightForeArm");

            LeftUpLeg = FindBoneRecursive(_cachedRoot, "char_LeftUpLeg");
            LeftLeg = FindBoneRecursive(_cachedRoot, "char_LeftLeg");
            LeftFoot = FindBoneRecursive(_cachedRoot, "char_LeftFoot");
            LeftToe = FindBoneRecursive(_cachedRoot, "char_LeftToeBase");

            RightUpLeg = FindBoneRecursive(_cachedRoot, "char_RightUpLeg");
            RightLeg = FindBoneRecursive(_cachedRoot, "char_RightLeg");
            RightFoot = FindBoneRecursive(_cachedRoot, "char_RightFoot");
            RightToe = FindBoneRecursive(_cachedRoot, "char_RightToeBase");

            _bonesResolved = (Hips != null && LeftUpLeg != null && LeftLeg != null && LeftFoot != null &&
                              RightUpLeg != null && RightLeg != null && RightFoot != null);

            if (_bonesResolved)
            {
                DebugLogger.Debug("Resolved player skeleton bones for DropkickAnimator (Hips=" + Hips.name +
                                  ", LeftFoot=" + LeftFoot.name + ", RightFoot=" + RightFoot.name + ").");
            }

            return _bonesResolved;
        }

        private static Transform FindBoneRecursive(Transform parent, string exactName)
        {
            if (parent == null)
                return null;

            if (string.Equals(parent.name, exactName, StringComparison.Ordinal))
                return parent;

            int childCount = parent.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform found = FindBoneRecursive(parent.GetChild(i), exactName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void SnapshotOrRestoreAnimatorPose()
        {
            if (_snapshotFrame != Time.frameCount)
            {
                _snapshotFrame = Time.frameCount;
                _baseHipsPos = Hips.position;
                _baseHipsRot = Hips.rotation;
                if (Spine1 != null) _baseSpine1Rot = Spine1.rotation;
                if (Spine2 != null) _baseSpine2Rot = Spine2.rotation;
                if (Spine3 != null) _baseSpine3Rot = Spine3.rotation;
                if (Neck != null) _baseNeckRot = Neck.rotation;
                if (Head != null) _baseHeadRot = Head.rotation;
                if (LeftUpLeg != null) _baseLeftUpLegRot = LeftUpLeg.rotation;
                if (LeftLeg != null) _baseLeftLegRot = LeftLeg.rotation;
                if (LeftFoot != null) _baseLeftFootRot = LeftFoot.rotation;
                if (RightUpLeg != null) _baseRightUpLegRot = RightUpLeg.rotation;
                if (RightLeg != null) _baseRightLegRot = RightLeg.rotation;
                if (RightFoot != null) _baseRightFootRot = RightFoot.rotation;
                if (LeftArm != null) _baseLeftArmRot = LeftArm.rotation;
                if (LeftForeArm != null) _baseLeftForeArmRot = LeftForeArm.rotation;
                if (RightArm != null) _baseRightArmRot = RightArm.rotation;
                if (RightForeArm != null) _baseRightForeArmRot = RightForeArm.rotation;
            }
            else
            {
                Hips.position = _baseHipsPos;
                Hips.rotation = _baseHipsRot;
                if (Spine1 != null) Spine1.rotation = _baseSpine1Rot;
                if (Spine2 != null) Spine2.rotation = _baseSpine2Rot;
                if (Spine3 != null) Spine3.rotation = _baseSpine3Rot;
                if (Neck != null) Neck.rotation = _baseNeckRot;
                if (Head != null) Head.rotation = _baseHeadRot;
                if (LeftUpLeg != null) LeftUpLeg.rotation = _baseLeftUpLegRot;
                if (LeftLeg != null) LeftLeg.rotation = _baseLeftLegRot;
                if (LeftFoot != null) LeftFoot.rotation = _baseLeftFootRot;
                if (RightUpLeg != null) RightUpLeg.rotation = _baseRightUpLegRot;
                if (RightLeg != null) RightLeg.rotation = _baseRightLegRot;
                if (RightFoot != null) RightFoot.rotation = _baseRightFootRot;
                if (LeftArm != null) LeftArm.rotation = _baseLeftArmRot;
                if (LeftForeArm != null) LeftForeArm.rotation = _baseLeftForeArmRot;
                if (RightArm != null) RightArm.rotation = _baseRightArmRot;
                if (RightForeArm != null) RightForeArm.rotation = _baseRightForeArmRot;
            }
        }

        /// <summary>
        /// Applies the procedural two-legged dropkick, gravity fall, ground impact, and get-up poses
        /// in LateUpdate after the Animator has updated.
        /// </summary>
        public void ApplyPoseLateUpdate()
        {
            if (_stateMachine == null || !_stateMachine.IsDropkicking)
            {
                HasDesiredCameraWorldPos = false;
                return;
            }

            if (!EnsureBonesResolved())
            {
                HasDesiredCameraWorldPos = false;
                return;
            }

            SnapshotOrRestoreAnimatorPose();

            Vector3 fwd = _stateMachine.KickDirection;
            if (fwd.sqrMagnitude < 0.001f)
            {
                fwd = LocalPlayer.Transform.forward;
                fwd.y = 0f;
                fwd.Normalize();
            }

            Vector3 aimFwd = _stateMachine.KickAimDirection;
            if (aimFwd.sqrMagnitude < 0.001f)
            {
                aimFwd = fwd;
            }

            Vector3 up = Vector3.up;
            Vector3 right = Vector3.Cross(up, fwd).normalized;

            // Standing eye reference based on clean Hips position (+0.62m above Hips)
            Vector3 standingEyePos = _baseHipsPos + up * 0.62f;

            switch (_stateMachine.CurrentPhase)
            {
                case DropkickPhase.Startup:
                case DropkickPhase.ActiveKick:
                case DropkickPhase.FallingToGround:
                    ApplyAirborneKickAndFallPose(fwd, aimFwd, up, right, standingEyePos);
                    break;

                case DropkickPhase.GroundImpact:
                    ApplyGroundImpactPose(fwd, up, right, standingEyePos);
                    break;

                case DropkickPhase.GettingUp:
                    ApplyGettingUpPose(fwd, up, right, standingEyePos);
                    break;
            }
        }

        private void ApplyAirborneKickAndFallPose(
            Vector3 fwd,
            Vector3 aimFwd,
            Vector3 up,
            Vector3 right,
            Vector3 standingEyePos)
        {
            float overallWeight = 1.0f;
            float tuckAmount = 0f;
            float extensionAmount = 0f;
            float fallBraceAmount = 0f;
            float impactPushPulse = 0f;

            if (_stateMachine.CurrentPhase == DropkickPhase.Startup)
            {
                float p = Mathf.Clamp01(_stateMachine.NormalizedTime / DropkickStateMachine.StartupEndNormalized);
                float smoothP = SmoothStep01(p);
                overallWeight = Mathf.Clamp01(p * 1.35f);
                // Rapidly chamber both knees up toward the chest before snapping forward
                tuckAmount = Mathf.Sin(p * Mathf.PI * 0.85f);
                extensionAmount = SmoothStep01(Mathf.Clamp01((p - 0.30f) / 0.70f));
            }
            else if (_stateMachine.CurrentPhase == DropkickPhase.ActiveKick)
            {
                float p = Mathf.Clamp01((_stateMachine.NormalizedTime - DropkickStateMachine.StartupEndNormalized) /
                                        (DropkickStateMachine.ActiveEndNormalized - DropkickStateMachine.StartupEndNormalized));
                overallWeight = 1.0f;
                tuckAmount = 0f;
                extensionAmount = 1.0f;

                if (_stateMachine.HasImpactedEnemy)
                {
                    // Visceral compression & push-off pulse when boots slam into an enemy
                    float sinceHit = Mathf.Clamp01((_stateMachine.TotalElapsed - _stateMachine.EnemyImpactTime) /
                                                   DropkickStateMachine.ImpactFollowThroughDuration);
                    impactPushPulse = Mathf.Sin(sinceHit * Mathf.PI);
                }
                else
                {
                    impactPushPulse = 0.08f * Mathf.Sin(p * Mathf.PI);
                }
            }
            else // DropkickPhase.FallingToGround
            {
                float fallT = Mathf.Clamp01(_stateMachine.PhaseElapsed / 0.42f);
                overallWeight = 1.0f;
                tuckAmount = 0.22f * fallT;
                extensionAmount = Mathf.Lerp(0.92f, 0.58f, SmoothStep01(fallT));
                fallBraceAmount = SmoothStep01(fallT);
            }

            // 1. Anchor desired camera world position slightly lower as body goes horizontal
            float camDropY = Mathf.Lerp(0.05f, 0.24f, fallBraceAmount);
            DesiredCameraWorldPos = standingEyePos - fwd * (0.10f * overallWeight) - up * camDropY;
            HasDesiredCameraWorldPos = true;

            // 2. Position Hips directly in front of and slightly below DesiredCameraWorldPos so both
            // thighs, knees, shins, and boots thrust right through the center-bottom of the first-person viewport!
            float forwardHipDist = 0.26f * extensionAmount + 0.14f * tuckAmount + 0.06f * impactPushPulse;
            float downHipDist = 0.31f * extensionAmount + 0.24f * tuckAmount + 0.06f * fallBraceAmount;

            Vector3 targetHipsPos = DesiredCameraWorldPos + (aimFwd * forwardHipDist) - (up * downHipDist);
            Hips.position = Vector3.Lerp(_baseHipsPos, targetHipsPos, overallWeight);

            // Pitch pelvis backward into a horizontal Dying Light flying dropkick posture
            float hipsPitchDeg = -46f * overallWeight * Mathf.Clamp01(extensionAmount + 0.4f * tuckAmount);
            Hips.rotation = Quaternion.AngleAxis(hipsPitchDeg, right) * Hips.rotation;

            // 3. Lean Spine/Torso/Head backward behind the camera lens so the upper torso is horizontal
            // in third-person and NEVER clips into the first-person camera frustum
            float spineLeanDeg = -18f * overallWeight;
            Quaternion spineLeanRot = Quaternion.AngleAxis(spineLeanDeg, right);
            if (Spine1 != null) Spine1.rotation = spineLeanRot * Spine1.rotation;
            if (Spine2 != null) Spine2.rotation = spineLeanRot * Spine2.rotation;
            if (Spine3 != null) Spine3.rotation = spineLeanRot * Spine3.rotation;
            if (Neck != null) Neck.rotation = Quaternion.AngleAxis(-10f * overallWeight, right) * Neck.rotation;
            if (Head != null) Head.rotation = Quaternion.AngleAxis(-10f * overallWeight, right) * Head.rotation;

            // 4. Pose Both Legs (Left & Right Thighs, Shins, Feet, Toes)
            // Chambered knee-tuck direction (knees pulled high into chest)
            Vector3 leftThighTuckDir = (aimFwd * 0.62f + up * 0.76f - right * 0.14f).normalized;
            Vector3 rightThighTuckDir = (aimFwd * 0.62f + up * 0.76f + right * 0.14f).normalized;

            // Full Dying Light two-legged snap extension direction (right boot slightly higher & canted inward,
            // left boot slightly alongside it, aimed directly into the lower-center of the viewport)
            float fallLegDrop = -0.22f * fallBraceAmount;
            Vector3 leftThighExtDir = (aimFwd * 0.94f + up * (0.20f + fallLegDrop) - right * 0.06f).normalized;
            Vector3 rightThighExtDir = (aimFwd * 0.94f + up * (0.25f + fallLegDrop) + right * 0.05f).normalized;

            Vector3 desiredLeftThighDir = Vector3.Slerp(leftThighTuckDir, leftThighExtDir, Mathf.Clamp01(extensionAmount)).normalized;
            Vector3 desiredRightThighDir = Vector3.Slerp(rightThighTuckDir, rightThighExtDir, Mathf.Clamp01(extensionAmount)).normalized;

            AlignBoneSegment(LeftUpLeg, LeftLeg, desiredLeftThighDir, overallWeight);
            AlignBoneSegment(RightUpLeg, RightLeg, desiredRightThighDir, overallWeight);

            // Shins: bent downward/back during chamber tuck -> snapped straight out during ActiveKick
            Vector3 leftShinTuckDir = (aimFwd * 0.20f - up * 0.96f - right * 0.08f).normalized;
            Vector3 rightShinTuckDir = (aimFwd * 0.20f - up * 0.96f + right * 0.08f).normalized;

            Vector3 leftShinExtDir = (aimFwd * 0.97f + up * (0.16f + fallLegDrop * 1.3f) - right * 0.03f).normalized;
            Vector3 rightShinExtDir = (aimFwd * 0.97f + up * (0.21f + fallLegDrop * 1.3f) + right * 0.03f).normalized;

            float shinExtBlend = Mathf.Clamp01(extensionAmount - 0.12f * impactPushPulse - 0.20f * fallBraceAmount);
            Vector3 desiredLeftShinDir = Vector3.Slerp(leftShinTuckDir, leftShinExtDir, shinExtBlend).normalized;
            Vector3 desiredRightShinDir = Vector3.Slerp(rightShinTuckDir, rightShinExtDir, shinExtBlend).normalized;

            AlignBoneSegment(LeftLeg, LeftFoot, desiredLeftShinDir, overallWeight);
            AlignBoneSegment(RightLeg, RightFoot, desiredRightShinDir, overallWeight);

            // Feet & Toes: dorsiflexed with slight Dying Light inward cant so both boot soles face the enemy
            if (LeftFoot != null && LeftToe != null)
            {
                Vector3 desiredLeftFootDir = (up * 0.90f - aimFwd * 0.28f + right * 0.14f).normalized;
                AlignBoneSegment(LeftFoot, LeftToe, desiredLeftFootDir, overallWeight * Mathf.Clamp01(extensionAmount));
            }
            if (RightFoot != null && RightToe != null)
            {
                Vector3 desiredRightFootDir = (up * 0.90f - aimFwd * 0.28f - right * 0.18f).normalized;
                AlignBoneSegment(RightFoot, RightToe, desiredRightFootDir, overallWeight * Mathf.Clamp01(extensionAmount));
            }

            // 5. Sweep both arms wide/back during the kick so held right-hand weapons don't clutter the center view,
            // and spread them outward to brace as gravity pulls the player toward the ground
            PoseArms(((-right * 0.82f) - (up * 0.32f) - (fwd * (0.35f - 0.25f * fallBraceAmount))).normalized,
                     ((right * 0.82f) - (up * 0.32f) - (fwd * (0.35f - 0.25f * fallBraceAmount))).normalized,
                     overallWeight * 0.85f);
        }

        private void ApplyGroundImpactPose(
            Vector3 fwd,
            Vector3 up,
            Vector3 right,
            Vector3 standingEyePos)
        {
            float p = _stateMachine.GroundImpactNormalizedTime;

            // Damped impact bounce when the player's back/hips and legs slam into the dirt
            float bounce = Mathf.Sin(p * Mathf.PI * 2.5f) * Mathf.Exp(-p * 3.5f);

            // Drop camera down near ground level (-0.68m from standing eye height)
            float dropAlpha = SmoothStep01(Mathf.Clamp01(p * 3.2f));
            float camDropOffset = Mathf.Lerp(0.24f, 0.68f, dropAlpha) + bounce * 0.04f;
            DesiredCameraWorldPos = standingEyePos - fwd * 0.18f - up * camDropOffset;
            HasDesiredCameraWorldPos = true;

            // Position pelvis on the ground in front of the low camera so the player sees their legs
            // bounce and sprawl out on the dirt in front of them
            Vector3 targetHipsPos = DesiredCameraWorldPos + fwd * 0.28f - up * 0.26f;
            Hips.position = targetHipsPos;
            Hips.rotation = Quaternion.AngleAxis(-52f, right) * Hips.rotation;

            // Keep upper torso/head leaned back behind the camera
            Quaternion spineLeanRot = Quaternion.AngleAxis(-20f, right);
            if (Spine1 != null) Spine1.rotation = spineLeanRot * Spine1.rotation;
            if (Spine2 != null) Spine2.rotation = spineLeanRot * Spine2.rotation;
            if (Spine3 != null) Spine3.rotation = spineLeanRot * Spine3.rotation;

            // Legs sprawl out on the ground in front of the player, bouncing slightly on impact
            float legBounceY = Mathf.Max(-0.08f, bounce * 0.22f);
            Vector3 leftThighGroundDir = (fwd * 0.92f + up * (0.12f + legBounceY) - right * 0.12f).normalized;
            Vector3 rightThighGroundDir = (fwd * 0.92f + up * (0.16f + legBounceY) + right * 0.12f).normalized;

            AlignBoneSegment(LeftUpLeg, LeftLeg, leftThighGroundDir, 1.0f);
            AlignBoneSegment(RightUpLeg, RightLeg, rightThighGroundDir, 1.0f);

            Vector3 leftShinGroundDir = (fwd * 0.95f - up * (0.10f - legBounceY * 0.5f) - right * 0.10f).normalized;
            Vector3 rightShinGroundDir = (fwd * 0.95f - up * (0.08f - legBounceY * 0.5f) + right * 0.10f).normalized;

            AlignBoneSegment(LeftLeg, LeftFoot, leftShinGroundDir, 1.0f);
            AlignBoneSegment(RightLeg, RightFoot, rightShinGroundDir, 1.0f);

            if (LeftFoot != null && LeftToe != null)
            {
                AlignBoneSegment(LeftFoot, LeftToe, (up * 0.85f + fwd * 0.25f - right * 0.25f).normalized, 0.9f);
            }
            if (RightFoot != null && RightToe != null)
            {
                AlignBoneSegment(RightFoot, RightToe, (up * 0.85f + fwd * 0.25f + right * 0.25f).normalized, 0.9f);
            }

            // Arms slap the ground to absorb the back-fall
            PoseArms((-right * 0.75f - up * 0.55f + fwd * 0.20f).normalized,
                     (right * 0.75f - up * 0.55f + fwd * 0.20f).normalized,
                     0.85f);
        }

        private void ApplyGettingUpPose(
            Vector3 fwd,
            Vector3 up,
            Vector3 right,
            Vector3 standingEyePos)
        {
            float p = _stateMachine.GettingUpNormalizedTime;

            // Phase A (0.00 -> 0.45): Roll onto side/hands, tuck knees underneath hips, plant hands on dirt
            // Phase B (0.45 -> 1.00): Push off the ground and rise smoothly back to standing height
            float standProgress = SmoothStep01(Mathf.Clamp01((p - 0.15f) / 0.85f));
            float overallWeight = 1.0f - SmoothStep01(Mathf.Clamp01((p - 0.35f) / 0.65f));

            float camDropOffset = Mathf.Lerp(0.68f, 0.0f, standProgress);
            float camBackOffset = Mathf.Lerp(0.18f, 0.0f, standProgress);
            DesiredCameraWorldPos = standingEyePos - fwd * camBackOffset - up * camDropOffset;
            HasDesiredCameraWorldPos = true;

            // Pelvis transitions from ground in front of camera back to underneath the standing spine
            Vector3 groundHipsPos = DesiredCameraWorldPos + fwd * 0.22f - up * 0.28f;
            Hips.position = Vector3.Lerp(_baseHipsPos, groundHipsPos, overallWeight);

            float hipsPitchDeg = Mathf.Lerp(-45f, 0f, standProgress);
            Hips.rotation = Quaternion.AngleAxis(hipsPitchDeg, right) * Hips.rotation;

            // Tuck legs underneath pelvis as the player pushes up onto their feet
            Vector3 leftThighCrouchDir = (fwd * 0.55f - up * 0.78f - right * 0.15f).normalized;
            Vector3 rightThighCrouchDir = (fwd * 0.65f - up * 0.70f + right * 0.15f).normalized;

            AlignBoneSegment(LeftUpLeg, LeftLeg, leftThighCrouchDir, overallWeight);
            AlignBoneSegment(RightUpLeg, RightLeg, rightThighCrouchDir, overallWeight);

            Vector3 leftShinCrouchDir = (-fwd * 0.45f - up * 0.88f).normalized;
            Vector3 rightShinCrouchDir = (-fwd * 0.25f - up * 0.95f).normalized;

            AlignBoneSegment(LeftLeg, LeftFoot, leftShinCrouchDir, overallWeight);
            AlignBoneSegment(RightLeg, RightFoot, rightShinCrouchDir, overallWeight);

            // Plant hands forward/down against the ground during the push-up
            float armPushWeight = Mathf.Sin(Mathf.Clamp01(p / 0.85f) * Mathf.PI) * 0.85f;
            PoseArms((fwd * 0.55f - up * 0.75f - right * 0.35f).normalized,
                     (fwd * 0.55f - up * 0.75f + right * 0.35f).normalized,
                     armPushWeight);
        }

        private void PoseArms(Vector3 leftDir, Vector3 rightDir, float weight)
        {
            if (weight <= 0.001f)
                return;

            if (LeftArm != null && LeftForeArm != null)
            {
                AlignBoneSegment(LeftArm, LeftForeArm, leftDir, weight);
            }
            if (RightArm != null && RightForeArm != null)
            {
                AlignBoneSegment(RightArm, RightForeArm, rightDir, weight);
            }
        }

        private static void AlignBoneSegment(Transform bone, Transform childBone, Vector3 targetWorldDir, float weight)
        {
            if (bone == null || childBone == null || weight <= 0.001f)
                return;

            Vector3 currentDir = childBone.position - bone.position;
            if (currentDir.sqrMagnitude < 0.00001f)
                return;

            currentDir.Normalize();
            Quaternion delta = Quaternion.FromToRotation(currentDir, targetWorldDir);
            Quaternion targetRot = delta * bone.rotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, targetRot, Mathf.Clamp01(weight));
        }

        private static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // =====================================================================
        // Harmony Postfix on playerAnimatorControl.LateUpdate to guarantee our
        // procedural skeletal pose runs right after the Animator updates.
        // =====================================================================

        [HarmonyPatch(typeof(playerAnimatorControl), "LateUpdate")]
        public static class Patch_PlayerAnimatorControl_LateUpdate
        {
            public static void Postfix()
            {
                if (Instance != null)
                {
                    Instance.ApplyPoseLateUpdate();
                }
            }
        }
    }
}
