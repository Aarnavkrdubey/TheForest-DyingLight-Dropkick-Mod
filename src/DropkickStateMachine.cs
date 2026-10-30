using System;
using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.UI.Multiplayer;
using TheForest.Utils;
using UnityEngine;

namespace TheForestDropkickMod
{
    public enum DropkickPhase
    {
        Idle = 0,
        Startup = 1,          // Airborne knee-tuck chamber & torso throw-back
        ActiveKick = 2,       // Full horizontal two-legged snap thrust & enemy impact
        FallingToGround = 3,  // Gravity pull downward after kick extension / enemy impact rebound
        GroundImpact = 4,     // Heavy back/hip landing impact & dirt skid on the ground
        GettingUp = 5         // Dying Light-style side roll, hand-plant, and push-up to standing
    }

    /// <summary>
    /// Core state machine and physics movement controller for the Dying Light-style airborne dropkick.
    /// Manages activation conditions, stamina cost, cooldown, one-kick-per-jump restriction,
    /// sprint momentum bonus, forward velocity impulse, enemy impact rebound, gravity fall,
    /// back-fall ground landing skid, and realistic push-up recovery.
    /// </summary>
    public sealed class DropkickStateMachine
    {
        public static DropkickStateMachine Instance { get; private set; }

        private readonly ModConfig _config;

        public DropkickPhase CurrentPhase { get; private set; }
        public float PhaseElapsed { get; private set; }
        public float TotalElapsed { get; private set; }
        public float CurrentDuration { get; private set; }
        public Vector3 KickDirection { get; private set; }
        public Vector3 KickAimDirection { get; private set; }
        public float MomentumMultiplier { get; private set; }
        public float ActiveForwardSpeed { get; private set; }
        public bool WasSprintingOnStart { get; private set; }
        public bool HasImpactedEnemy { get; private set; }
        public float EnemyImpactTime { get; private set; }
        public bool HitWallDuringKick { get; private set; }

        private bool _usedDropkickInCurrentJump;
        private float _lastDropkickEndTime = -999f;
        private float _postDropkickFallImmunityUntil;

        // Normalized phase boundaries within CurrentDuration (for the airborne kick portion)
        public const float StartupEndNormalized = 0.20f;
        public const float ActiveEndNormalized = 0.78f;

        // Fixed durations for the Dying Light gravity ground-fall and get-up sequence
        public const float ImpactFollowThroughDuration = 0.085f;
        public const float MaxFallingToGroundDuration = 1.85f;
        public const float GroundImpactDuration = 0.36f;
        public const float GettingUpDuration = 0.56f;

        public bool IsDropkicking
        {
            get { return CurrentPhase != DropkickPhase.Idle; }
        }

        public bool IsInAirborneKickPhase
        {
            get
            {
                return CurrentPhase == DropkickPhase.Startup ||
                       CurrentPhase == DropkickPhase.ActiveKick ||
                       CurrentPhase == DropkickPhase.FallingToGround;
            }
        }

        public bool IsInGroundRecoveryPhase
        {
            get
            {
                return CurrentPhase == DropkickPhase.GroundImpact ||
                       CurrentPhase == DropkickPhase.GettingUp;
            }
        }

        public float NormalizedTime
        {
            get
            {
                if (CurrentDuration <= 0.001f)
                    return 0f;
                return Mathf.Clamp01(TotalElapsed / CurrentDuration);
            }
        }

        public float GroundImpactNormalizedTime
        {
            get { return Mathf.Clamp01(PhaseElapsed / GroundImpactDuration); }
        }

        public float GettingUpNormalizedTime
        {
            get { return Mathf.Clamp01(PhaseElapsed / GettingUpDuration); }
        }

        public DropkickStateMachine(ModConfig config)
        {
            _config = config;
            CurrentPhase = DropkickPhase.Idle;
            MomentumMultiplier = 1f;
            KickDirection = Vector3.forward;
            KickAimDirection = Vector3.forward;
            Instance = this;
        }

        public void Update(
            DropkickHitDetector hitDetector,
            DropkickCameraFeedback cameraFeedback,
            EnemyCombatIntegration combatIntegration)
        {
            if (!_config.DropkickEnabled)
            {
                if (IsDropkicking)
                {
                    ForceCancelDropkick("Mod disabled in config");
                }
                return;
            }

            if (!IsLocalPlayerReady())
            {
                if (IsDropkicking)
                {
                    ForceCancelDropkick("LocalPlayer not ready");
                }
                return;
            }

            FirstPersonCharacter fp = LocalPlayer.FpCharacter;
            bool isGrounded = IsPlayerEffectivelyGrounded(fp);

            if (isGrounded && !IsDropkicking)
            {
                _usedDropkickInCurrentJump = false;
            }

            if (!IsDropkicking)
            {
                // Check for Dropkick activation input
                if (UnityEngine.Input.GetKeyDown(_config.DropkickKey))
                {
                    TryStartDropkick(fp, isGrounded, hitDetector, cameraFeedback);
                }
            }
            else
            {
                UpdateActiveDropkick(fp, isGrounded, hitDetector, cameraFeedback, combatIntegration);
            }
        }

        public void FixedUpdate()
        {
            if (!IsDropkicking || !IsLocalPlayerReady())
                return;

            FirstPersonCharacter fp = LocalPlayer.FpCharacter;
            Rigidbody rb = LocalPlayer.Rigidbody;
            if (fp == null || rb == null)
                return;

            ApplyDropkickPhysics(fp, rb);
        }

        private bool IsLocalPlayerReady()
        {
            return LocalPlayer.Transform != null &&
                    LocalPlayer.FpCharacter != null &&
                    LocalPlayer.Rigidbody != null &&
                    LocalPlayer.Stats != null &&
                    LocalPlayer.AnimControl != null &&
                    LocalPlayer.Inventory != null;
        }

        /// <summary>
        /// Combines FirstPersonCharacter.Grounded with a short downward raycast check so that
        /// standing on slightly uneven terrain never false-positives as airborne, while genuine jumps
        /// and ground impacts after a dropkick are accurately detected.
        /// </summary>
        private bool IsPlayerEffectivelyGrounded(FirstPersonCharacter fp)
        {
            if (fp.Grounded || GameReflection.GetFauxGrounded(fp))
                return true;

            // During the upward launch of a jump or kick, only treat as airborne if moving upward fast
            if (CurrentPhase != DropkickPhase.FallingToGround &&
                CurrentPhase != DropkickPhase.GroundImpact &&
                CurrentPhase != DropkickPhase.GettingUp)
            {
                if (fp.jumping || (LocalPlayer.Rigidbody != null && LocalPlayer.Rigidbody.velocity.y > 1.0f))
                    return false;
            }

            // Check if feet/capsule bottom are touching or very close to the ground
            CapsuleCollider cap = fp.capsule;
            float rayDist = (CurrentPhase == DropkickPhase.FallingToGround) ? 0.34f : 0.22f;
            if (cap != null)
            {
                Vector3 bottom = cap.bounds.center - new Vector3(0f, cap.bounds.extents.y - 0.06f, 0f);
                if (Physics.Raycast(bottom, Vector3.down, rayDist, fp.CollisionLayers, QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }
            else if (LocalPlayer.Transform != null)
            {
                Vector3 origin = LocalPlayer.Transform.position + Vector3.up * 0.15f;
                if (Physics.Raycast(origin, Vector3.down, rayDist + 0.15f, fp.CollisionLayers, QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanActivateDropkick(FirstPersonCharacter fp, bool isGrounded, out string blockReason)
        {
            blockReason = null;

            // 1. Must not be in menus, inventory, survival book, or chat
            if (LocalPlayer.IsInPauseMenu || LocalPlayer.IsInInventory || LocalPlayer.IsInBook ||
                ChatBox.IsChatOpen || UICamera.inputHasFocus ||
                LocalPlayer.Inventory.CurrentView != PlayerInventory.PlayerViews.World)
            {
                blockReason = "Menu, inventory, book, or chat is open";
                return false;
            }

            // 2. Must be airborne (not grounded)
            if (isGrounded)
            {
                blockReason = "Player is grounded";
                return false;
            }

            // 3. Only one dropkick activation per jump
            if (_usedDropkickInCurrentJump)
            {
                blockReason = "Already executed a dropkick during this jump";
                return false;
            }

            // 4. Cooldown check
            float timeSinceLast = Time.time - _lastDropkickEndTime;
            if (timeSinceLast < _config.Cooldown)
            {
                blockReason = string.Format("On cooldown ({0:F2}s remaining)", _config.Cooldown - timeSinceLast);
                return false;
            }

            // 5. Restricted player states (swimming, climbing, sitting, sleeping, sled, raft, glider, incapacitated, cutscene)
            playerAnimatorControl anim = LocalPlayer.AnimControl;
            PlayerStats stats = LocalPlayer.Stats;

            if (stats.Dead || anim.knockedDown)
            {
                blockReason = "Player is dead or incapacitated";
                return false;
            }

            if (fp.swimming || fp.Diving || anim.swimming || fp.IsInWater())
            {
                blockReason = "Player is swimming or in water";
                return false;
            }

            if (anim.onRope || anim.cliffClimb || fp.Locked || fp.MovementLocked)
            {
                blockReason = "Player is climbing or movement-locked";
                return false;
            }

            if (fp.Sitting || anim.sitting || fp.SailingRaft || fp.PushingSled ||
                anim.doSledPushMode || anim.doShellRideMode || anim.flyingGlider || anim.holdingGlider)
            {
                blockReason = "Player is sitting, sleeping, or operating a vehicle/glider/sled";
                return false;
            }

            if (anim.endGameCutScene || anim.introCutScene || anim.enteringACave || anim.exitingACave || anim.useRootMotion)
            {
                blockReason = "Player is in a cutscene or cave transition";
                return false;
            }

            // 6. Stamina check
            if (_config.StaminaConsumption && _config.StaminaCost > 0f)
            {
                if (stats.Stamina < Mathf.Min(5f, _config.StaminaCost))
                {
                    if (LocalPlayer.Sfx != null)
                    {
                        LocalPlayer.Sfx.PlayStaminaBreath();
                    }
                    blockReason = "Insufficient stamina (" + stats.Stamina.ToString("F1") + ")";
                    return false;
                }
            }

            return true;
        }

        private void TryStartDropkick(
            FirstPersonCharacter fp,
            bool isGrounded,
            DropkickHitDetector hitDetector,
            DropkickCameraFeedback cameraFeedback)
        {
            string reason;
            if (!CanActivateDropkick(fp, isGrounded, out reason))
            {
                DebugLogger.Debug("Dropkick activation blocked: " + reason);
                return;
            }

            Rigidbody rb = LocalPlayer.Rigidbody;
            Vector3 horizVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            float horizSpeed = horizVel.magnitude;

            // Detect sprint momentum
            bool isSprinting = fp.run || fp.running ||
                               TheForest.Utils.Input.GetButton("Run") ||
                               UnityEngine.Input.GetKey(KeyCode.LeftShift) ||
                               horizSpeed > (fp.walkSpeed * 1.15f);

            WasSprintingOnStart = isSprinting && _config.SprintMomentumBonus;

            // Calculate momentum multiplier (1.0x standing/walking jump -> up to 1.32x full sprint jump)
            if (WasSprintingOnStart)
            {
                float speedFactor = Mathf.Clamp01(horizSpeed / Mathf.Max(1f, fp.runSpeed));
                MomentumMultiplier = Mathf.Lerp(1.15f, 1.32f, speedFactor);
            }
            else
            {
                MomentumMultiplier = 1.0f;
            }

            // Determine attack direction from camera / player forward on the horizontal plane
            Vector3 rawCamFwd = LocalPlayer.MainCamTr != null
                ? LocalPlayer.MainCamTr.forward
                : LocalPlayer.Transform.forward;

            Vector3 forward = new Vector3(rawCamFwd.x, 0f, rawCamFwd.z);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = LocalPlayer.Transform.forward;
                forward.y = 0f;
            }
            KickDirection = forward.normalized;

            // View-aligned kick aim vector (clamped vertical pitch so legs aim naturally toward the crosshair)
            float clampedPitchY = Mathf.Clamp(rawCamFwd.y, -0.22f, 0.28f);
            KickAimDirection = (KickDirection + Vector3.up * clampedPitchY).normalized;

            // Target forward boost speed (clamped within balanced range, e.g. 7.2 m/s base, ~8.8 m/s sprinting)
            ActiveForwardSpeed = Mathf.Clamp(_config.DropkickForce * MomentumMultiplier, 4.5f, 12.5f);
            CurrentDuration = _config.DropkickDuration;
            TotalElapsed = 0f;
            PhaseElapsed = 0f;
            HasImpactedEnemy = false;
            EnemyImpactTime = -1f;
            HitWallDuringKick = false;
            _usedDropkickInCurrentJump = true;
            _postDropkickFallImmunityUntil = Time.time + CurrentDuration + GroundImpactDuration + GettingUpDuration + 2.0f;

            // Consume stamina
            if (_config.StaminaConsumption && _config.StaminaCost > 0f)
            {
                LocalPlayer.Stats.Stamina = Mathf.Max(0f, LocalPlayer.Stats.Stamina - _config.StaminaCost);
            }

            // Reset hit detector for this new dropkick
            if (hitDetector != null)
            {
                hitDetector.BeginDropkick(LocalPlayer.ScriptSetup);
            }

            // Apply initial upward + forward launch velocity so the kick has hangtime like Dying Light
            if (!CheckWallAhead(fp, KickDirection, 0.85f))
            {
                float newY = Mathf.Max(rb.velocity.y, 2.55f);
                rb.velocity = KickDirection * (ActiveForwardSpeed * 0.90f) + new Vector3(0f, newY, 0f);
            }

            CurrentPhase = DropkickPhase.Startup;

            if (cameraFeedback != null)
            {
                cameraFeedback.OnDropkickStart(WasSprintingOnStart);
            }

            DebugLogger.Info(string.Format(
                "Dropkick started | Sprint={0} | Momentum={1:F2}x | Speed={2:F2}m/s | Duration={3:F2}s",
                WasSprintingOnStart, MomentumMultiplier, ActiveForwardSpeed, CurrentDuration));
        }

        private void UpdateActiveDropkick(
            FirstPersonCharacter fp,
            bool isGrounded,
            DropkickHitDetector hitDetector,
            DropkickCameraFeedback cameraFeedback,
            EnemyCombatIntegration combatIntegration)
        {
            float dt = Time.deltaTime;
            TotalElapsed += dt;
            PhaseElapsed += dt;

            // Global safety checks: cancel immediately if player enters water, climbs rope, dies, or exceeds max total time
            float maxTotalDuration = CurrentDuration + MaxFallingToGroundDuration + GroundImpactDuration + GettingUpDuration + 0.5f;
            if (TotalElapsed > maxTotalDuration ||
                LocalPlayer.Stats.Dead ||
                LocalPlayer.AnimControl.knockedDown ||
                fp.swimming ||
                fp.Diving ||
                fp.IsInWater() ||
                LocalPlayer.AnimControl.onRope ||
                LocalPlayer.AnimControl.cliffClimb)
            {
                EndDropkick();
                return;
            }

            // Keep KickAimDirection smoothly tracking camera pitch slightly during Startup/ActiveKick so legs stay framed
            if (CurrentPhase == DropkickPhase.Startup || CurrentPhase == DropkickPhase.ActiveKick)
            {
                if (LocalPlayer.MainCamTr != null)
                {
                    float pitchY = Mathf.Clamp(LocalPlayer.MainCamTr.forward.y, -0.22f, 0.28f);
                    Vector3 targetAim = (KickDirection + Vector3.up * pitchY).normalized;
                    KickAimDirection = Vector3.Slerp(KickAimDirection, targetAim, dt * 14f).normalized;
                }
            }

            float norm = NormalizedTime;

            switch (CurrentPhase)
            {
                case DropkickPhase.Startup:
                {
                    if (norm >= StartupEndNormalized)
                    {
                        CurrentPhase = DropkickPhase.ActiveKick;
                        PhaseElapsed = 0f;
                        if (cameraFeedback != null)
                        {
                            cameraFeedback.OnDropkickExtension();
                        }
                    }
                    break;
                }

                case DropkickPhase.ActiveKick:
                {
                    // Run collision-based hit detection throughout ActiveKick
                    if (hitDetector != null && combatIntegration != null)
                    {
                        int hitsThisFrame = hitDetector.EvaluateHits(
                            LocalPlayer.ScriptSetup,
                            KickDirection,
                            MomentumMultiplier,
                            WasSprintingOnStart,
                            combatIntegration,
                            cameraFeedback);

                        if (hitsThisFrame > 0 && !HasImpactedEnemy)
                        {
                            HasImpactedEnemy = true;
                            EnemyImpactTime = TotalElapsed;
                            ApplyEnemyImpactRecoil();
                        }
                    }

                    // After hitting an enemy and completing the brief boot push-off follow-through,
                    // transition into FallingToGround (or GroundImpact if already touching the ground)
                    if (HasImpactedEnemy && (TotalElapsed - EnemyImpactTime) >= ImpactFollowThroughDuration)
                    {
                        if (isGrounded)
                        {
                            EnterGroundImpactPhase(cameraFeedback);
                        }
                        else
                        {
                            CurrentPhase = DropkickPhase.FallingToGround;
                            PhaseElapsed = 0f;
                        }
                        return;
                    }

                    // If the player touches ground during the second half of ActiveKick, crash-land onto back
                    if (isGrounded && norm >= 0.48f)
                    {
                        EnterGroundImpactPhase(cameraFeedback);
                        return;
                    }

                    // Once the midair kick extension finishes, gravity pulls the horizontal player down to the ground
                    if (norm >= ActiveEndNormalized)
                    {
                        if (isGrounded)
                        {
                            EnterGroundImpactPhase(cameraFeedback);
                        }
                        else
                        {
                            CurrentPhase = DropkickPhase.FallingToGround;
                            PhaseElapsed = 0f;
                        }
                    }
                    break;
                }

                case DropkickPhase.FallingToGround:
                {
                    // Still allow hit detection briefly during the start of the downward arc if we haven't hit yet
                    if (!HasImpactedEnemy && PhaseElapsed < 0.16f && hitDetector != null && combatIntegration != null)
                    {
                        int hitsThisFrame = hitDetector.EvaluateHits(
                            LocalPlayer.ScriptSetup,
                            KickDirection,
                            MomentumMultiplier,
                            WasSprintingOnStart,
                            combatIntegration,
                            cameraFeedback);

                        if (hitsThisFrame > 0)
                        {
                            HasImpactedEnemy = true;
                            EnemyImpactTime = TotalElapsed;
                            ApplyEnemyImpactRecoil();
                        }
                    }

                    if (isGrounded || PhaseElapsed >= MaxFallingToGroundDuration)
                    {
                        EnterGroundImpactPhase(cameraFeedback);
                    }
                    break;
                }

                case DropkickPhase.GroundImpact:
                {
                    // Player has crashed onto their back/hip on the dirt after the dropkick
                    if (PhaseElapsed >= GroundImpactDuration)
                    {
                        CurrentPhase = DropkickPhase.GettingUp;
                        PhaseElapsed = 0f;
                        if (cameraFeedback != null)
                        {
                            cameraFeedback.OnStartGettingUp();
                        }
                    }
                    break;
                }

                case DropkickPhase.GettingUp:
                {
                    // Player rolls onto hands/side and pushes themselves back up to standing
                    if (PhaseElapsed >= GettingUpDuration)
                    {
                        EndDropkick();
                    }
                    break;
                }
            }
        }

        private void EnterGroundImpactPhase(DropkickCameraFeedback cameraFeedback)
        {
            CurrentPhase = DropkickPhase.GroundImpact;
            PhaseElapsed = 0f;

            Rigidbody rb = LocalPlayer.Rigidbody;
            if (rb != null)
            {
                // Keep a short skid along the ground without popping back into the air
                Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                if (horiz.magnitude > 3.2f)
                {
                    horiz = horiz.normalized * 3.2f;
                }
                rb.velocity = new Vector3(horiz.x, Mathf.Min(rb.velocity.y, -1.0f), horiz.z);
            }

            if (cameraFeedback != null)
            {
                cameraFeedback.OnGroundFallImpact(HasImpactedEnemy, MomentumMultiplier);
            }

            DebugLogger.Debug("Dropkick landed on back -> Entering GroundImpact & GettingUp recovery.");
        }

        private void ApplyDropkickPhysics(FirstPersonCharacter fp, Rigidbody rb)
        {
            // Prevent accidental self fall-damage throughout the dropkick and ground fall
            fp.allowFallDamage = false;
            GameReflection.SetFallShakeBlock(fp, true);

            float fdt = Time.fixedDeltaTime;

            switch (CurrentPhase)
            {
                case DropkickPhase.Startup:
                {
                    if (!HitWallDuringKick && CheckWallAhead(fp, KickDirection, 0.75f))
                    {
                        HitWallDuringKick = true;
                        rb.velocity = new Vector3(0f, Mathf.Min(rb.velocity.y, 0f), 0f);
                        return;
                    }

                    Vector3 desiredHoriz = KickDirection * (ActiveForwardSpeed * 0.92f);
                    float desiredY = Mathf.Max(rb.velocity.y, 1.95f);
                    rb.velocity = new Vector3(desiredHoriz.x, desiredY, desiredHoriz.z);
                    break;
                }

                case DropkickPhase.ActiveKick:
                {
                    if (!HitWallDuringKick && !HasImpactedEnemy && CheckWallAhead(fp, KickDirection, 0.75f))
                    {
                        HitWallDuringKick = true;
                        rb.velocity = new Vector3(0f, Mathf.Min(rb.velocity.y, -0.5f), 0f);
                        DebugLogger.Debug("Dropkick forward boost halted due to solid wall ahead.");
                        return;
                    }

                    if (HitWallDuringKick || HasImpactedEnemy)
                        return;

                    float norm = NormalizedTime;
                    float phaseProgress = Mathf.Clamp01((norm - StartupEndNormalized) /
                                                        (ActiveEndNormalized - StartupEndNormalized));
                    float speedScale = Mathf.Lerp(1.05f, 0.80f, phaseProgress);
                    Vector3 desiredHoriz = KickDirection * (ActiveForwardSpeed * speedScale);

                    // Hangtime suspension during peak extension, then start letting gravity pull downward
                    float gravAssist = (phaseProgress < 0.55f) ? (fp.gravity * 0.36f * fdt) : (-fp.gravity * 0.25f * fdt);
                    float velY = rb.velocity.y + gravAssist;
                    rb.velocity = new Vector3(desiredHoriz.x, velY, desiredHoriz.z);
                    break;
                }

                case DropkickPhase.FallingToGround:
                {
                    // Realistic gravity pull: accelerate the horizontal body down to the ground
                    float extraDownwardAccel = fp.gravity * 0.55f * fdt;
                    float newY = rb.velocity.y - Mathf.Abs(extraDownwardAccel);
                    if (PhaseElapsed > 0.08f && newY > -2.2f)
                    {
                        newY = Mathf.Lerp(newY, -5.5f, fdt * 8f);
                    }

                    // Gently damp horizontal air velocity while falling
                    Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                    if (!CheckWallAhead(fp, horiz.normalized, 0.65f))
                    {
                        horiz = Vector3.Lerp(horiz, Vector3.zero, fdt * 2.8f);
                    }
                    else
                    {
                        horiz = Vector3.zero;
                    }

                    rb.velocity = new Vector3(horiz.x, newY, horiz.z);
                    break;
                }

                case DropkickPhase.GroundImpact:
                {
                    // Player is lying on their back/side on the ground: skid to a stop with dirt friction
                    Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                    horiz = Vector3.Lerp(horiz, Vector3.zero, fdt * 11f);
                    rb.velocity = new Vector3(horiz.x, Mathf.Min(rb.velocity.y, -0.5f), horiz.z);
                    break;
                }

                case DropkickPhase.GettingUp:
                {
                    // Keep horizontal movement stationary during the hand-plant & push-up (first 75% of GettingUp),
                    // then smoothly blend movement back in the final 25%
                    float getUpNorm = GettingUpNormalizedTime;
                    if (getUpNorm < 0.75f)
                    {
                        Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                        horiz = Vector3.Lerp(horiz, Vector3.zero, fdt * 14f);
                        rb.velocity = new Vector3(horiz.x, Mathf.Min(rb.velocity.y, 0f), horiz.z);
                    }
                    break;
                }
            }
        }

        private void ApplyEnemyImpactRecoil()
        {
            Rigidbody rb = LocalPlayer.Rigidbody;
            if (rb == null)
                return;

            // Newton's 3rd Law (Dying Light style):
            // Both boots slam into the enemy, pushing the enemy violently backward while the player
            // rebounds backward slightly in midair before gravity pulls them down onto their back!
            Vector3 recoilVel = (-KickDirection * 2.25f) + new Vector3(0f, 1.45f, 0f);
            rb.velocity = recoilVel;
        }

        private bool CheckWallAhead(FirstPersonCharacter fp, Vector3 dir, float distance)
        {
            if (LocalPlayer.Transform == null || dir.sqrMagnitude < 0.001f)
                return false;

            Vector3 origin = LocalPlayer.Transform.position + Vector3.up * 0.5f;
            RaycastHit hit;
            if (Physics.SphereCast(origin, 0.32f, dir.normalized, out hit, distance, fp.CollisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && !IsEnemyOrPlayerCollider(hit.collider))
                {
                    if (hit.normal.y < 0.55f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsEnemyOrPlayerCollider(Collider col)
        {
            if (col == null)
                return false;

            GameObject go = col.gameObject;
            if (go.CompareTag("Player") || go.CompareTag("PlayerNet") ||
                go.CompareTag("enemyCollide") || go.CompareTag("enemyHead") ||
                go.CompareTag("EnemyBodyPart") || go.CompareTag("animalCollide"))
            {
                return true;
            }

            if (LocalPlayer.Transform != null && col.transform.root == LocalPlayer.Transform.root)
                return true;

            if (col.GetComponentInParent<EnemyHealth>() != null || col.GetComponentInParent<mutantHitReceiver>() != null)
                return true;

            return false;
        }

        private void EndDropkick()
        {
            CurrentPhase = DropkickPhase.Idle;
            PhaseElapsed = 0f;
            TotalElapsed = 0f;
            _lastDropkickEndTime = Time.time;
            _usedDropkickInCurrentJump = false;

            if (LocalPlayer.FpCharacter != null)
            {
                LocalPlayer.FpCharacter.allowFallDamage = false;
            }

            DebugLogger.Debug("Dropkick & get-up recovery completed cleanly; returned to normal movement.");
        }

        public void ForceCancelDropkick(string reason)
        {
            if (!IsDropkicking)
                return;

            CurrentPhase = DropkickPhase.Idle;
            PhaseElapsed = 0f;
            TotalElapsed = 0f;
            _lastDropkickEndTime = Time.time;
            _usedDropkickInCurrentJump = false;
            DebugLogger.Debug("Dropkick cancelled: " + reason);
        }

        public bool IsInPostDropkickFallImmunity()
        {
            return IsDropkicking || Time.time <= _postDropkickFallImmunityUntil;
        }

        // =====================================================================
        // Harmony Patches on FirstPersonCharacter to prevent velocity clamping
        // and accidental self-damage when landing from a dropkick
        // =====================================================================

        [HarmonyPatch(typeof(FirstPersonCharacter), "clampVelocityTowardEnemy")]
        public static class Patch_FpCharacter_ClampVelocityTowardEnemy
        {
            public static bool Prefix(FirstPersonCharacter __instance)
            {
                if (Instance != null && Instance.IsDropkicking)
                {
                    __instance.clampInputVal = 1f;
                    __instance.doingClampInput = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(FirstPersonCharacter), "ClampVelocity")]
        public static class Patch_FpCharacter_ClampVelocity
        {
            public static bool Prefix()
            {
                if (Instance != null && Instance.IsDropkicking)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(FirstPersonCharacter), "FixedUpdate")]
        public static class Patch_FpCharacter_FixedUpdate
        {
            public static void Postfix(FirstPersonCharacter __instance)
            {
                if (Instance == null)
                    return;

                if (Instance.IsInPostDropkickFallImmunity())
                {
                    __instance.allowFallDamage = false;
                }

                if (Instance.IsDropkicking)
                {
                    Instance.FixedUpdate();
                }
            }
        }

        [HarmonyPatch(typeof(FirstPersonCharacter), "HandleLanded")]
        public static class Patch_FpCharacter_HandleLanded
        {
            public static void Prefix(FirstPersonCharacter __instance)
            {
                if (Instance != null && Instance.IsInPostDropkickFallImmunity())
                {
                    __instance.allowFallDamage = false;
                }
            }
        }
    }
}
