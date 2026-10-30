using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheForest.UI;
using UnityEngine;

namespace TheForestDropkickMod
{
    /// <summary>
    /// Pre-cached reflection accessors for non-public fields and methods in Assembly-CSharp.dll.
    /// All FieldInfo and MethodInfo handles are resolved once at class initialization for zero runtime overhead.
    /// </summary>
    public static class GameReflection
    {
        // InputMappingIcons & ActionIconSystem
        private static readonly FieldInfo KeyboardMappingsField =
            AccessTools.Field(typeof(InputMappingIcons), "KeyboardMappings");

        private static readonly FieldInfo ActionIconSystemInstanceField =
            AccessTools.Field(typeof(ActionIconSystem), "Instance");

        private static readonly FieldInfo ActiveIconsField =
            AccessTools.Field(typeof(ActionIconSystem), "_activeIcons");

        // FirstPersonCharacter
        private static readonly FieldInfo FpFauxGroundedField =
            AccessTools.Field(typeof(FirstPersonCharacter), "FauxGrounded");

        private static readonly FieldInfo FpFallShakeBlockField =
            AccessTools.Field(typeof(FirstPersonCharacter), "fallShakeBlock");

        // EnemyHealth
        private static readonly FieldInfo EhDeadBlockField =
            AccessTools.Field(typeof(EnemyHealth), "deadBlock");

        private static readonly FieldInfo EhSetupField =
            AccessTools.Field(typeof(EnemyHealth), "setup");

        private static readonly FieldInfo EhAiField =
            AccessTools.Field(typeof(EnemyHealth), "ai");

        private static readonly FieldInfo EhAnimatorField =
            AccessTools.Field(typeof(EnemyHealth), "animator");

        private static readonly MethodInfo EhBloodMethod =
            AccessTools.Method(typeof(EnemyHealth), "Blood");

        // mutantHitReceiver
        private static readonly FieldInfo MhrHealthField =
            AccessTools.Field(typeof(mutantHitReceiver), "health");

        private static readonly MethodInfo MhrGetSkinHitPositionMethod =
            AccessTools.Method(typeof(mutantHitReceiver), "getSkinHitPosition", new Type[] { typeof(Transform) });

        public static string[] GetKeyboardMappings()
        {
            if (KeyboardMappingsField == null)
                return null;
            return KeyboardMappingsField.GetValue(null) as string[];
        }

        public static ActionIconSystem GetActionIconSystemInstance()
        {
            if (ActionIconSystemInstanceField == null)
                return null;
            return ActionIconSystemInstanceField.GetValue(null) as ActionIconSystem;
        }

        public static Dictionary<Transform, ActionIcon> GetActiveIcons()
        {
            ActionIconSystem sys = GetActionIconSystemInstance();
            if (sys == null || ActiveIconsField == null)
                return null;
            return ActiveIconsField.GetValue(sys) as Dictionary<Transform, ActionIcon>;
        }

        public static bool GetFauxGrounded(FirstPersonCharacter fp)
        {
            if (fp == null || FpFauxGroundedField == null)
                return false;
            return (bool)FpFauxGroundedField.GetValue(fp);
        }

        public static void SetFallShakeBlock(FirstPersonCharacter fp, bool value)
        {
            if (fp != null && FpFallShakeBlockField != null)
            {
                FpFallShakeBlockField.SetValue(fp, value);
            }
        }

        public static bool GetEnemyDeadBlock(EnemyHealth eh)
        {
            if (eh == null || EhDeadBlockField == null)
                return false;
            return (bool)EhDeadBlockField.GetValue(eh);
        }

        public static mutantScriptSetup GetEnemySetup(EnemyHealth eh)
        {
            if (eh == null)
                return null;
            if (EhSetupField != null)
            {
                var s = EhSetupField.GetValue(eh) as mutantScriptSetup;
                if (s != null) return s;
            }
            return eh.GetComponent<mutantScriptSetup>() ?? eh.GetComponentInParent<mutantScriptSetup>();
        }

        public static mutantAI GetEnemyAI(EnemyHealth eh)
        {
            if (eh == null)
                return null;
            if (EhAiField != null)
            {
                var ai = EhAiField.GetValue(eh) as mutantAI;
                if (ai != null) return ai;
            }
            mutantScriptSetup setup = GetEnemySetup(eh);
            if (setup != null && setup.ai != null)
                return setup.ai;
            return eh.GetComponent<mutantAI>() ?? eh.GetComponentInParent<mutantAI>();
        }

        public static Animator GetEnemyAnimator(EnemyHealth eh)
        {
            if (eh == null)
                return null;
            if (EhAnimatorField != null)
            {
                var anim = EhAnimatorField.GetValue(eh) as Animator;
                if (anim != null) return anim;
            }
            mutantScriptSetup setup = GetEnemySetup(eh);
            if (setup != null && setup.animator != null)
                return setup.animator;
            return eh.GetComponent<Animator>() ?? eh.GetComponentInChildren<Animator>();
        }

        public static void InvokeEnemyBlood(EnemyHealth eh)
        {
            if (eh != null && EhBloodMethod != null)
            {
                EhBloodMethod.Invoke(eh, null);
            }
        }

        public static EnemyHealth GetReceiverHealth(mutantHitReceiver receiver)
        {
            if (receiver == null || MhrHealthField == null)
                return null;
            return MhrHealthField.GetValue(receiver) as EnemyHealth;
        }

        public static void InvokeGetSkinHitPosition(mutantHitReceiver receiver, Transform hitTr)
        {
            if (receiver != null && hitTr != null && MhrGetSkinHitPositionMethod != null)
            {
                MhrGetSkinHitPositionMethod.Invoke(receiver, new object[] { hitTr });
            }
        }
    }
}
