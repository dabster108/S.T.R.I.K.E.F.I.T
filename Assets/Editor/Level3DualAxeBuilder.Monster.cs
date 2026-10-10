// Level3DualAxeBuilder.Monster.cs
// Purpose: The Level 3 boss (Mixamo Ch25, Humanoid, one axe per hand):
//   1. Humanoid clips baked from the project's Mixamo mocap (HumanoidClipBaker): dual-wield attacks are one-handed
//      swings with the left arm driven by the mirrored swing (same maths as AnimatorState.mirror), poses with no mocap
//      (idle, guard, roar) are arm muscle values fitted to axe targets on this rig.
//   2. Animator controller with the Level 1/2 transition rules: every attack / Defend / Roar enters from both idles,
//      from Hit, from Defend and from Roar; blends into Idle are interruptible; Any State is used ONLY for Death.
//   3. Prefab: summoning-circle entrance, bone-following hit volumes, glowing right-forearm weak point, two axes with
//      their own MonsterWeapon clash colliders, enrage aura/embers, unblockable slam danger zone.
//   4. Attack table whose wind-ups are the MEASURED impact frames (blade-edge speed peak / ground contact) of the
//      baked clips on the built prefab, so damage lands on the visible hit.
// Menu: Tools > Level 3 > Build Dual-Axe Monster

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class Level3DualAxeBuilder
{
    public const string PrefabPath = "Assets/Prefabs/Monsters/Monster_Hard_TwinAxeBrute.prefab";
    public const string AnimDir = "Assets/Animations/Monster/Level3";
    public const string ControllerPath = AnimDir + "/Monster_L3_TwinAxe.controller";
    public const string WeakMeshPath = "Assets/Models/Monsters/Level3/Ch25_WeakPointSplit.asset";
    const string SfxDir = "Assets/Audio/SFX/Level3";
    const string Mocap = "Assets/Animations/Monster/";
    public const float ModelScale = 1.25f; // Ch25 is 1.91 m; the boss stands 2.39 m

    // ======================================================================= poses (right side; the left mirrors)
    // Absolute muscle values, fitted on Ch25 + the gripped axe from the symmetric dual ready stance (random search over the
    // arm muscles through HumanPoseHandler, minimising hand-position and haft/edge-direction error to these targets):
    // IDLE   right hand (0.40, 0.84, 0.20), haft (0.42, 0.40, 0.81), edge down: axes held out at the sides, blades hanging
    // DEFEND right hand (0.23, 1.12, 0.33), haft (-0.50, 0.86, 0.10), edge forward: hafts cross above the fists, blades in front of the face
    // ROAR   right hand (0.65, 1.04, 0.11), haft (0.51, 0.78, 0.36): arms flung wide, axes raised outward
    static readonly Dictionary<string, float> PoseIdle = new Dictionary<string, float>
    {
        {"Shoulder Down-Up", -1.000f}, {"Shoulder Front-Back", -1.000f}, {"Arm Down-Up", -0.111f}, {"Arm Front-Back", 0.334f}, {"Arm Twist In-Out", 0.389f},
        {"Forearm Stretch", 0.173f}, {"Forearm Twist In-Out", 0.512f}, {"Hand Down-Up", 0.254f}, {"Hand In-Out", 1.000f},
    };
    static readonly Dictionary<string, float> PoseDefend = new Dictionary<string, float>
    {
        {"Shoulder Down-Up", -0.048f}, {"Shoulder Front-Back", 0.741f}, {"Arm Down-Up", -1.000f}, {"Arm Front-Back", -1.000f}, {"Arm Twist In-Out", 1.000f},
        {"Forearm Stretch", -0.224f}, {"Forearm Twist In-Out", -1.000f}, {"Hand Down-Up", -0.400f}, {"Hand In-Out", -0.334f},
    };
    static readonly Dictionary<string, float> PoseRoar = new Dictionary<string, float>
    {
        {"Shoulder Down-Up", 1.000f}, {"Shoulder Front-Back", -1.000f}, {"Arm Down-Up", -0.211f}, {"Arm Front-Back", 0.263f}, {"Arm Twist In-Out", 0.807f},
        {"Forearm Stretch", 1.000f}, {"Forearm Twist In-Out", 0.609f}, {"Hand Down-Up", -0.993f}, {"Hand In-Out", 0.971f},
    };

    // ======================================================================= clip specs
    public class AttackClip
    {
        public string state;              // state == trigger == clip name
        public Vector2[] windows;          // where each hit is searched (seconds in the baked clip)
        public bool groundContact;         // hit = blade reaching the ground (slam)
        public bool frontMost;             // hit = blades furthest forward (spins: the speed peak is on the backswing)
                                           // otherwise hit = blade-edge speed peak (the Level 2 convention)
        public float[] hits;               // measured
        public float length;
        public float catchTime;            // throws: thrown axes back in the hand (clip seconds)
        public Vector2 travel;             // rush: run window (clip seconds)
    }

    public static readonly List<AttackClip> AttackClips = new List<AttackClip>();

    static HumanoidTrack Src(string file, string clip = "mixamo.com") => HumanoidTrack.FromClip(Mocap + file, clip);
    static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    /// <summary>Both arms lerped toward an absolute pose by w(t).</summary>
    static HumanoidTrack Posed(HumanoidTrack t, Dictionary<string, float> pose, Func<float, float> w)
    {
        return HumanoidTrack.Generate(t.curves.Keys, t.length, (p, time) =>
        {
            float v = t.Sample(p, time);
            string key = p.StartsWith("Right ") ? p.Substring(6) : (p.StartsWith("Left ") ? p.Substring(5) : null);
            float target;
            if (key != null && pose.TryGetValue(key, out target)) return Mathf.Lerp(v, target, Mathf.Clamp01(w(time)));
            return v;
        });
    }

    /// <summary>Raise jumps less: RootT.y above its first-frame height is scaled (crouches untouched).</summary>
    static HumanoidTrack DampJump(HumanoidTrack t, float keep)
    {
        float y0 = t.Sample("RootT.y", 0f);
        return t.Offset((p, time) => p == "RootT.y" ? -(1f - keep) * Mathf.Max(0f, t.Sample(p, time) - y0) : 0f);
    }

    /// <summary>Start from and end in the idle pose so every state blend is short and clean.</summary>
    static HumanoidTrack FromToIdle(HumanoidTrack idleHold, HumanoidTrack x, float blendIn = 0.18f, float blendOut = 0.45f)
    {
        var start = idleHold.Hold(0f, blendIn);
        return start.Concat(x, blendIn).Concat(idleHold.Hold(0f, blendOut + 0.15f), blendOut);
    }

    public static string BuildClips()
    {
        if (!AssetDatabase.IsValidFolder(AnimDir)) AssetDatabase.CreateFolder("Assets/Animations/Monster", "Level3");
        var log = new System.Text.StringBuilder();
        AttackClips.Clear();

        var H = Src("Level2/Standing Melee Attack Horizontal.fbx");          // one-handed horizontal sweep
        var D = Src("Level2/Standing Melee Attack Downward.fbx");            // one-handed overhead chop
        var OH = Src("Monster_SwordAttack.fbx");                             // One Hand Sword Combo
        var GS = Src("Great Sword Slash.fbx");
        var CV1 = Src("Level2/Standing Melee Combo Attack Ver. 1.fbx");
        var SP = Src("Great Sword High Spin Attack.fbx");
        var RJ = Src("Level2/Standing Melee Run Jump Attack.fbx");
        var C3 = Src("Standing Melee Combo Attack Ver. 3.fbx");
        var SS = Src("Level1/Sword And Shield Slash (L1).fbx");               // spinning leap slash
        var MJ = Src("Mutant Jump Attack.fbx").WithFists(H);                 // two-fisted leap + ground smash
        var HR = Src("Monster_HitReaction.fbx").WithFists(H);
        var DE = HumanoidTrack.FromClip("Assets/Models/Monsters/Two Handed Sword Death.fbx", "death").WithFists(H);

        // Dual ready stance (symmetric) -> idle pose.
        var stance = HumanoidTrack.DualWield(H, H.Mirror(), 0f, t => 0.5f).Hold(0f, 1f);
        var idleHold = Posed(stance, PoseIdle, t => 1f);

        // --- Idle: slow heavy breathing (3.2 s loop) ---
        float w1 = 2f * Mathf.PI / 3.2f;
        var idle = HumanoidTrack.Generate(idleHold.curves.Keys, 3.2f, (p, t) => idleHold.Sample(p, 0f) + Breath(p, t, w1, 1f));
        Save(idle, "L3_Idle", true, log);

        // --- Enraged idle: hunched, arms wider, fast deep breathing and a tremor (1.2 s loop) ---
        float w2 = 2f * Mathf.PI / 1.2f, wt = 2f * Mathf.PI * 10f / 1.2f;
        var idleE = HumanoidTrack.Generate(idleHold.curves.Keys, 1.2f, (p, t) =>
        {
            float v = idleHold.Sample(p, 0f) + Breath(p, t, w2, 2.2f);
            if (p == "Spine Front-Back") v += 0.12f;
            else if (p == "Chest Front-Back") v += 0.08f;
            else if (p == "Head Nod Down-Up") v -= 0.12f;
            else if (p.EndsWith("Arm Down-Up")) v += 0.10f;
            else if (p.EndsWith("Shoulder Down-Up")) v += 0.15f;
            else if (p.EndsWith("Arm Twist In-Out")) v += 0.025f * Mathf.Sin(wt * t);
            else if (p.EndsWith("Forearm Twist In-Out")) v += 0.02f * Mathf.Cos(wt * t);
            return v;
        });
        Save(idleE, "L3_IdleEnraged", true, log);

        // --- Entrance: crouched with both axes in the ground (Mutant Jump Attack landing), rising to full height ---
        const float crouchAt = 1.70f;
        Save(MJ.Hold(crouchAt, 1f), "L3_Underground", true, log);
        var rise = MJ.Retime(new Vector2(0f, crouchAt), new Vector2(3.0f, MJ.length)).Concat(idleHold.Hold(0f, 0.6f), 0.5f);
        Save(rise, "L3_Rise", false, log);

        // --- Roar: chest out, head thrown back, axes flung wide, trembling (2.4 s) ---
        Func<float, float> env = t => Smooth(0f, 0.35f, t) * (1f - Smooth(1.75f, 2.4f, t));
        var roarBase = Posed(idleHold.Hold(0f, 2.4f), PoseRoar, env);
        var roar = roarBase.Offset((p, t) =>
        {
            float e = env(t), shake = Smooth(0.3f, 0.45f, t) * (1f - Smooth(1.6f, 1.8f, t));
            switch (p)
            {
                case "Chest Front-Back": return -0.35f * e;
                case "UpperChest Front-Back": return -0.30f * e;
                case "Spine Front-Back": return -0.15f * e;
                case "Head Nod Down-Up": return 0.55f * e;
                case "Neck Nod Down-Up": return 0.20f * e;
                case "Head Turn Left-Right": return 0.04f * shake * Mathf.Sin(2f * Mathf.PI * 7f * t);
                case "Right Arm Down-Up": case "Left Arm Down-Up": return 0.04f * shake * Mathf.Sin(2f * Mathf.PI * 11f * t);
            }
            return 0f;
        });
        Save(roar, "L3_Roar", false, log);

        // --- Defend: both axes crossed in front of the face (1.0 s: 0.15 in, hold, 0.15 out) ---
        Func<float, float> guard = t => Smooth(0f, 0.15f, t) * (1f - Smooth(0.85f, 1.0f, t));
        var defend = Posed(idleHold.Hold(0f, 1.0f), PoseDefend, guard)
            .Offset((p, t) => p == "Spine Front-Back" ? 0.08f * guard(t) : (p == "Head Nod Down-Up" ? -0.10f * guard(t) : 0f));
        Save(defend, "L3_Defend", false, log);

        // --- Hit reaction / death (mocap, fists closed on the axes) ---
        Save(FromToIdle(idleHold, HR.Retime(new Vector2(0f, 0f), new Vector2(0.9f, 0.9f)), 0.1f, 0.35f), "L3_Hit", false, log);
        Save(idleHold.Hold(0f, 0.15f).Concat(DE, 0.15f), "L3_Death", false, log);

        // --- Attacks. Retime(...) compresses each source's slow anticipation so wind-ups are shorter than Level 2's,
        //     while the swing itself (last ~0.2 s before impact) keeps close to mocap speed. ---
        // 1. Alternating Chops (standard, 2 hits): right chop, then the left axe chops (mirrored, 0.45 s later).
        var oh1 = OH.Retime(new Vector2(0f, 0f), new Vector2(1.55f, 1.55f));
        var alt = HumanoidTrack.DualWield(oh1, oh1.Mirror(), 0.45f, t => Smooth(0.95f, 1.35f, t))
            .Retime(new Vector2(0f, 0f), new Vector2(0.40f, 0.88f), new Vector2(0.58f, 1.083f), new Vector2(0.94f, 1.533f), new Vector2(1.407f, 2.0f));
        Attack("Atk_AltChops", FromToIdle(idleHold, alt), log, new Vector2(0.45f, 0.72f), new Vector2(0.80f, 1.10f));

        // 2. Crossing Double Swing (standard): both axes sweep inward and cross in front.
        var cross = HumanoidTrack.DualWield(H, H.Mirror(), 0f, t => 0.5f)
            .Retime(new Vector2(0f, 0f), new Vector2(0.36f, 0.75f), new Vector2(0.56f, 0.983f), new Vector2(1.977f, 2.40f));
        Attack("Atk_CrossSwing", FromToIdle(idleHold, cross), log, new Vector2(0.42f, 0.70f));

        // 3. Twin Diagonal Hack (standard): both axes up, X-cut down and outward.
        var hack = HumanoidTrack.DualWield(GS, GS.Mirror(), 0f, t => 0.5f)
            .Retime(new Vector2(0f, 0f), new Vector2(0.33f, 0.70f), new Vector2(0.53f, 0.933f), new Vector2(1.427f, 1.83f));
        Attack("Atk_TwinHack", FromToIdle(idleHold, hack), log, new Vector2(0.40f, 0.68f));

        // 4. Reaping Flurry (combo, 3 hits): left, right, then both.
        var flurry = HumanoidTrack.DualWield(CV1, CV1.Mirror(), 0f, t => 0f)
            .Retime(new Vector2(0f, 0f), new Vector2(0.40f, 1.45f), new Vector2(0.62f, 1.75f), new Vector2(0.88f, 2.067f), new Vector2(1.60f, 2.95f), new Vector2(3.32f, 4.67f));
        Attack("Atk_ReapFlurry", FromToIdle(idleHold, flurry), log, new Vector2(0.50f, 0.76f), new Vector2(0.77f, 1.05f), new Vector2(1.40f, 1.80f));

        // 5. Twin Whirlwind (heavy): full spin, both axes out at head height.
        var whirl = HumanoidTrack.DualWield(SP, SP.Mirror(), 0f, t => 0f)
            .Retime(new Vector2(0f, 0f), new Vector2(0.65f, 0.90f), new Vector2(0.85f, 1.133f), new Vector2(1.587f, 1.87f));
        Attack("Atk_TwinWhirl", FromToIdle(idleHold, whirl), log, new Vector2(0.70f, 1.15f)).frontMost = true;

        // 6. Twin Overhead Cleave (heavy): both axes raised overhead, a held beat, then slammed down together.
        var cleave = HumanoidTrack.DualWield(D, D.Mirror(), 0f, t => 0.5f)
            .Retime(new Vector2(0f, 0f), new Vector2(0.50f, 0.43f), new Vector2(0.75f, 0.60f), new Vector2(0.95f, 0.85f), new Vector2(2.37f, 2.27f));
        Attack("Atk_TwinCleave", FromToIdle(idleHold, cleave), log, new Vector2(0.80f, 1.10f));

        // 7. Leaping Double Cleave (heavy, phase 2+): leap with both axes overhead, cleave on landing.
        var leap = DampJump(HumanoidTrack.DualWield(RJ, RJ.Mirror(), 0f, t => 0.5f), 0.4f)
            .Retime(new Vector2(0f, 0.6f), new Vector2(0.55f, 1.45f), new Vector2(0.80f, 1.717f), new Vector2(2.753f, 3.67f));
        Attack("Atk_LeapCleave", FromToIdle(idleHold, leap), log, new Vector2(0.65f, 0.95f));

        // 9. Rending Double Strike (combo, 2 hits): chest-height strike with the right axe, the left follows 0.30 s later.
        var c3a = C3.Retime(new Vector2(0f, 0f), new Vector2(1.35f, 1.35f));
        var rend = HumanoidTrack.DualWield(c3a, c3a.Mirror(), 0.30f, t => Smooth(0.85f, 1.25f, t))
            .Retime(new Vector2(0f, 0f), new Vector2(0.32f, 0.75f), new Vector2(0.52f, 0.983f), new Vector2(0.80f, 1.283f), new Vector2(1.17f, 1.65f));
        Attack("Atk_RendStrike", FromToIdle(idleHold, rend), log, new Vector2(0.40f, 0.66f), new Vector2(0.67f, 0.95f));

        // 10. Spinning Reap (standard): turn away, spin back with a hop and sweep both axes across the front.
        var reap = DampJump(HumanoidTrack.DualWield(SS, SS.Mirror(), 0f, t => 0f), 0.5f)
            .Retime(new Vector2(0f, 0f), new Vector2(0.55f, 1.15f), new Vector2(0.72f, 1.383f), new Vector2(1.767f, 2.43f));
        Attack("Atk_SpinReap", FromToIdle(idleHold, reap), log, new Vector2(0.58f, 0.88f));

        // 11. Backhand Double Chop (standard, 2 hits): low backswing, then right and left overhead chops.
        var ohb = OH.Retime(new Vector2(0f, 1.55f), new Vector2(2.05f, 3.6f));
        var back = HumanoidTrack.DualWield(ohb, ohb.Mirror(), 0.30f, t => Smooth(1.35f, 1.70f, t))
            .Retime(new Vector2(0f, 0f), new Vector2(0.35f, 1.20f), new Vector2(0.52f, 1.467f), new Vector2(0.80f, 1.767f), new Vector2(1.383f, 2.35f));
        Attack("Atk_BackChop", FromToIdle(idleHold, back), log, new Vector2(0.40f, 0.66f), new Vector2(0.67f, 0.95f)).frontMost = true; // fastest frames are the raise

        // 8. Earthsplitter Slam (special, UNBLOCKABLE): leap, both axes smashed into the dock.
        var slam = DampJump(MJ, 0.4f)
            .Retime(new Vector2(0f, 0f), new Vector2(1.0f, 1.30f), new Vector2(1.25f, 1.62f), new Vector2(3.33f, 3.70f));
        var slamClip = Attack("Atk_GroundSlam", FromToIdle(idleHold, slam), log, new Vector2(1.10f, 1.45f));
        slamClip.groundContact = true;

        // --- Ranged attacks (chosen only when the player is out of melee reach) ---
        // 12. Hurled Axe: the overhead chop (Standing Melee Attack Downward) as an overhand throw with the right axe,
        //     released on the blade's speed peak; the arm then stays reached out until the axe flies back into the fist.
        //     The left axe is held in its idle grip throughout.
        var thrR = D.Retime(new Vector2(0f, 0f), new Vector2(0.42f, 0.43f), new Vector2(0.64f, 0.62f), new Vector2(0.80f, 0.85f),
                            new Vector2(0.98f, 1.00f), new Vector2(1.90f, 1.00f), new Vector2(2.25f, 1.30f));
        var thr = thrR.Override(idleHold.Hold(0f, thrR.length), HumanoidTrack.LeftArm);
        var thrClip = Attack("Atk_AxeThrow", FromToIdle(idleHold, thr, 0.18f, 0.45f), log, new Vector2(0.55f, 0.95f));
        thrClip.catchTime = 1.85f;

        // 13. Twin Hurl (combo, phase 2+): right throw, then the mirrored left throw 0.40 s later; both arms reach out
        //     for the catch.
        var twin = HumanoidTrack.DualWield(thrR, thrR.Mirror(), 0.40f, t => Smooth(0.70f, 1.15f, t));
        var twinClip = Attack("Atk_TwinHurl", FromToIdle(idleHold, twin, 0.18f, 0.45f), log, new Vector2(0.55f, 0.95f), new Vector2(0.95f, 1.35f));
        twinClip.catchTime = 2.20f;

        // 14. Brute Rush (heavy gap-closer): brace with both axes cocked back (the Crossing Double Swing's wound pose),
        //     sprint (Standing Melee Run Jump Attack's run steps; the root moves for real in MonsterAI), then the crossing
        //     double sweep on arrival.
        var crossRaw = HumanoidTrack.DualWield(H, H.Mirror(), 0f, t => 0.5f);
        var run = RJ.Retime(new Vector2(0f, 0f), new Vector2(0.85f, 0.80f));
        var cocked = crossRaw.Hold(0.70f, run.length);
        var runArmed = run.Override(cocked, p => HumanoidTrack.LeftArm(p) || HumanoidTrack.RightArm(p));
        var brace = runArmed.Hold(0f, 0.30f);
        var strike = crossRaw.Retime(new Vector2(0f, 0.70f), new Vector2(0.25f, 0.983f), new Vector2(1.55f, 2.40f));
        var rush = brace.Concat(runArmed, 0.12f).Concat(strike, 0.15f);
        var rushClip = Attack("Atk_Rush", FromToIdle(idleHold, rush, 0.18f, 0.45f), log, new Vector2(0.95f, 1.40f));
        rushClip.travel = new Vector2(0.18f, 0.98f);

        // --- New in this pass. No unused Mixamo clip fits (the project's only unused attack clip is the Level 1 sword-and-shield
        //     slash, a one-handed cut), so these three are new COMPOSITIONS of the mocap the boss already uses. ---
        // 15. Twin Cyclone (heavy, phase 2+): the whirlwind's spin twice. Same wind-up, then a second, faster turn before the recovery.
        var whirlRaw = HumanoidTrack.DualWield(SP, SP.Mirror(), 0f, t => 0f);
        var cycA = whirlRaw.Retime(new Vector2(0f, 0f), new Vector2(0.65f, 0.90f), new Vector2(0.85f, 1.133f), new Vector2(1.45f, 1.80f));
        var cycB = whirlRaw.Retime(new Vector2(0f, 1.00f), new Vector2(0.60f, 1.87f), new Vector2(1.10f, 1.87f));
        var cyclone = cycA.Concat(cycB, 0.18f);
        Attack("Atk_TwinCyclone", FromToIdle(idleHold, cyclone), log, new Vector2(0.70f, 1.15f), new Vector2(1.55f, 2.10f)).frontMost = true;

        // 16. Twin Overhead Chain (combo, 2 hits): both axes slam down, are snatched back up and slam again, faster.
        var cleaveRaw = HumanoidTrack.DualWield(D, D.Mirror(), 0f, t => 0.5f);
        var ovA = cleaveRaw.Retime(new Vector2(0f, 0f), new Vector2(0.50f, 0.43f), new Vector2(0.75f, 0.60f), new Vector2(0.95f, 0.85f), new Vector2(1.20f, 1.15f));
        var ovB = cleaveRaw.Retime(new Vector2(0f, 0.40f), new Vector2(0.30f, 0.60f), new Vector2(0.48f, 0.85f), new Vector2(1.60f, 2.27f));
        var chain2 = ovA.Concat(ovB, 0.30f);
        Attack("Atk_OverheadChain", FromToIdle(idleHold, chain2), log, new Vector2(0.80f, 1.10f), new Vector2(1.45f, 1.80f));

        // 17. Lunge Strike (heavy, ranged-row: the player 1.8-3.0 m away): a short brace, two quick strides at the player and the
        //     crossing double sweep on arrival. The shorter, closer cousin of the Brute Rush.
        var runShort = RJ.Retime(new Vector2(0f, 0f), new Vector2(0.50f, 0.50f));
        var crockedShort = crossRaw.Hold(0.70f, runShort.length);
        var runShortArmed = runShort.Override(crockedShort, p => HumanoidTrack.LeftArm(p) || HumanoidTrack.RightArm(p));
        var braceShort = runShortArmed.Hold(0f, 0.28f);
        var lungeStrike = crossRaw.Retime(new Vector2(0f, 0.70f), new Vector2(0.22f, 0.983f), new Vector2(1.40f, 2.40f));
        var lunge = braceShort.Concat(runShortArmed, 0.10f).Concat(lungeStrike, 0.12f);
        var lungeClip = Attack("Atk_LungeStrike", FromToIdle(idleHold, lunge, 0.18f, 0.45f), log, new Vector2(0.62f, 1.00f));
        lungeClip.travel = new Vector2(0.14f, 0.58f);

        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static float Breath(string p, float t, float w, float amp)
    {
        float s = Mathf.Sin(w * t);
        switch (p)
        {
            case "Spine Front-Back": return 0.025f * amp * s;
            case "Chest Front-Back": return 0.035f * amp * s;
            case "UpperChest Front-Back": return 0.030f * amp * s;
            case "Head Nod Down-Up": return -0.04f * amp * Mathf.Sin(w * t + 0.8f);
            case "Right Shoulder Down-Up": case "Left Shoulder Down-Up": return 0.06f * amp * (0.5f + 0.5f * s);
            case "Right Arm Down-Up": case "Left Arm Down-Up": return 0.025f * amp * Mathf.Sin(w * t - 0.6f);
            case "RootT.y": return 0.006f * amp * s;
        }
        return 0f;
    }

    static AnimationClip Save(HumanoidTrack t, string name, bool loop, System.Text.StringBuilder log)
    {
        var c = t.Bake($"{AnimDir}/{name}.anim", loop);
        log.AppendLine($"  {name}: {t.length:F2} s{(loop ? " loop" : "")}");
        return c;
    }

    static AttackClip Attack(string state, HumanoidTrack t, System.Text.StringBuilder log, params Vector2[] windows)
    {
        Save(t, state, false, log);
        var a = new AttackClip { state = state, windows = windows, length = t.length };
        AttackClips.Add(a);
        return a;
    }

    static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{name}.anim");

    // ======================================================================= controller
    public static readonly string[] AttackStates = { "Atk_AltChops", "Atk_CrossSwing", "Atk_TwinHack", "Atk_ReapFlurry", "Atk_TwinWhirl", "Atk_TwinCleave", "Atk_LeapCleave", "Atk_GroundSlam", "Atk_RendStrike", "Atk_SpinReap", "Atk_BackChop", "Atk_AxeThrow", "Atk_TwinHurl", "Atk_Rush", "Atk_TwinCyclone", "Atk_OverheadChain", "Atk_LungeStrike" };

    public static string BuildController()
    {
        // Rebuild in place (never delete): the controller keeps its GUID, so the prefab's reference stays valid.
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else
        {
            foreach (var p in ctrl.parameters) ctrl.RemoveParameter(p);
            var old = ctrl.layers[0].stateMachine;
            foreach (var t in old.anyStateTransitions) old.RemoveAnyStateTransition(t);
            foreach (var cs in old.states) old.RemoveState(cs.state);
        }
        var triggers = new List<string> { "Idle", "Hit", "Death", "Defend", "Rise", "Roar" };
        triggers.AddRange(AttackStates);
        foreach (var t in triggers) ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Enraged", AnimatorControllerParameterType.Bool);

        var sm = ctrl.layers[0].stateMachine;
        Func<string, string, Vector2, AnimatorState> S = (state, clip, pos) =>
        {
            var st = sm.AddState(state, pos); st.motion = Clip(clip); st.writeDefaultValues = true; st.iKOnFeet = false; return st;
        };
        var underground = S("Underground", "L3_Underground", new Vector2(-300, 0));
        var rise = S("Rise", "L3_Rise", new Vector2(-300, 100));
        var roar = S("Roar", "L3_Roar", new Vector2(-300, 200));
        var idle = S("Idle", "L3_Idle", new Vector2(0, 0));
        var idleE = S("IdleEnraged", "L3_IdleEnraged", new Vector2(0, 120));
        var hit = S("Hit", "L3_Hit", new Vector2(250, -150));
        var defend = S("Defend", "L3_Defend", new Vector2(250, -60));
        var death = S("Death", "L3_Death", new Vector2(-300, 320));
        sm.defaultState = underground;
        var attackStates = new List<AnimatorState>();
        for (int i = 0; i < AttackStates.Length; i++) attackStates.Add(S(AttackStates[i], AttackStates[i], new Vector2(500, i * 70)));

        // Entrance
        Trig(underground, rise, "Rise", 0.05f);
        Trig(rise, roar, "Roar", 0.15f);
        ExitTo(rise, idle, 1f, 0.2f);
        ExitToIdle(roar, idle, idleE, 0.98f, 0.25f);

        // Idle <-> enraged idle (bool flips while idling). Interruptible by the destination, so an attack / defend
        // trigger set during this 0.2 s blend is taken at once instead of waiting for the blend to finish.
        var toE = idle.AddTransition(idleE); toE.hasExitTime = false; toE.duration = 0.2f; toE.AddCondition(AnimatorConditionMode.If, 0, "Enraged");
        toE.interruptionSource = TransitionInterruptionSource.Destination;
        var toN = idleE.AddTransition(idle); toN.hasExitTime = false; toN.duration = 0.2f; toN.AddCondition(AnimatorConditionMode.IfNot, 0, "Enraged");
        toN.interruptionSource = TransitionInterruptionSource.Destination;

        // Every action starts from either idle, from Hit (a flinch never delays a swing / guard / roar), from the
        // tail of Defend and from Roar (the AI resumes before the enrage roar clip ends).
        foreach (var src in new[] { idle, idleE, hit, defend, roar })
        {
            for (int i = 0; i < AttackStates.Length; i++) Trig(src, attackStates[i], AttackStates[i], 0.08f);
            if (src != defend) Trig(src, defend, "Defend", 0.08f);
            if (src != roar) Trig(src, roar, "Roar", 0.12f);
        }
        Trig(idle, hit, "Hit", 0.05f, true);
        Trig(idleE, hit, "Hit", 0.05f, true);

        // Back to the idle that matches the phase (Idle, or IdleEnraged while Enraged) on the Idle trigger (EndAttack /
        // end of defend / flinch) or by exit time; interruptible. Going straight to IdleEnraged matters: a detour through
        // Idle -> IdleEnraged delayed every enraged action that started within that blend.
        foreach (var st in attackStates) { TrigToIdle(st, idle, idleE, 0.12f); ExitToIdle(st, idle, idleE, 0.98f, 0.12f); }
        TrigToIdle(hit, idle, idleE, 0.1f); ExitToIdle(hit, idle, idleE, 0.95f, 0.1f);
        TrigToIdle(defend, idle, idleE, 0.12f); ExitToIdle(defend, idle, idleE, 0.98f, 0.12f);
        TrigToIdle(roar, idle, idleE, 0.2f);

        // Any State -> Death only.
        var d = sm.AddAnyStateTransition(death); d.hasExitTime = false; d.duration = 0.1f; d.canTransitionToSelf = false; d.AddCondition(AnimatorConditionMode.If, 0, "Death");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        int n = sm.anyStateTransitions.Length; foreach (var s in sm.states) n += s.state.transitions.Length;
        return $"Controller {ControllerPath}: {sm.states.Length} states, {n} transitions, Any State -> Death only.";
    }

    static void Trig(AnimatorState from, AnimatorState to, string trigger, float blend, bool interruptibleByNext = false)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false; t.duration = blend; t.hasFixedDuration = true; t.offset = 0f;
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        if (interruptibleByNext) t.interruptionSource = TransitionInterruptionSource.Destination;
    }

    static void TrigToIdle(AnimatorState from, AnimatorState idle, AnimatorState idleE, float blend)
    {
        var a = from.AddTransition(idle);
        a.hasExitTime = false; a.duration = blend; a.hasFixedDuration = true; a.interruptionSource = TransitionInterruptionSource.Destination;
        a.AddCondition(AnimatorConditionMode.If, 0, "Idle"); a.AddCondition(AnimatorConditionMode.IfNot, 0, "Enraged");
        var b = from.AddTransition(idleE);
        b.hasExitTime = false; b.duration = blend; b.hasFixedDuration = true; b.interruptionSource = TransitionInterruptionSource.Destination;
        b.AddCondition(AnimatorConditionMode.If, 0, "Idle"); b.AddCondition(AnimatorConditionMode.If, 0, "Enraged");
    }

    static void ExitToIdle(AnimatorState from, AnimatorState idle, AnimatorState idleE, float exitTime, float blend)
    {
        var a = from.AddTransition(idle);
        a.hasExitTime = true; a.exitTime = exitTime; a.duration = blend; a.hasFixedDuration = true; a.interruptionSource = TransitionInterruptionSource.Destination;
        a.AddCondition(AnimatorConditionMode.IfNot, 0, "Enraged");
        var b = from.AddTransition(idleE);
        b.hasExitTime = true; b.exitTime = exitTime; b.duration = blend; b.hasFixedDuration = true; b.interruptionSource = TransitionInterruptionSource.Destination;
        b.AddCondition(AnimatorConditionMode.If, 0, "Enraged");
    }

    static void ExitTo(AnimatorState from, AnimatorState to, float exitTime, float blend)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true; t.exitTime = exitTime; t.duration = blend; t.hasFixedDuration = true;
        t.interruptionSource = TransitionInterruptionSource.Destination;
    }

    // ======================================================================= weak point mesh
    /// <summary>Ch25 mesh with the right forearm (triangles skinned mainly to RightForeArm) as its own submesh 2.</summary>
    static Mesh BuildWeakPointMesh(SkinnedMeshRenderer smr, out int tris)
    {
        var src = smr.sharedMesh;
        int bi = Array.FindIndex(smr.bones, b => b.name == "mixamorig:RightForeArm");
        var bw = src.boneWeights;
        var copy = Object.Instantiate(src);
        copy.name = "Ch25_WeakPointSplit";
        var weak = new List<int>();
        copy.subMeshCount = src.subMeshCount + 1;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            var keep = new List<int>();
            var tri = src.GetTriangles(s);
            for (int k = 0; k < tri.Length; k += 3)
            {
                int c = 0; for (int j = 0; j < 3; j++) if (bw[tri[k + j]].boneIndex0 == bi) c++;
                var dst = (c >= 2 && s == 0) ? weak : keep;
                dst.Add(tri[k]); dst.Add(tri[k + 1]); dst.Add(tri[k + 2]);
            }
            copy.SetTriangles(keep, s);
        }
        copy.SetTriangles(weak, src.subMeshCount);
        tris = weak.Count / 3;
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(WeakMeshPath);
        if (existing != null) { EditorUtility.CopySerialized(copy, existing); Object.DestroyImmediate(copy); copy = existing; }
        else AssetDatabase.CreateAsset(copy, WeakMeshPath);
        return copy;
    }

    // ======================================================================= prefab
    static AudioClip Sfx(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{name}.wav");
    static AudioClip ClipAt(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    static Material L3Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

    static Transform Child(Transform parent, string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = localPos; return t;
    }

    static Transform Bone(GameObject root, string n)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "mixamorig:" + n) return t;
        throw new Exception("Bone missing: " + n);
    }

    public static string BuildPrefab()
    {
        var log = new System.Text.StringBuilder();
        // Built in an isolated preview scene: the open level scene is never touched.
        var buildScene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Monster_Hard_TwinAxeBrute");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, buildScene);
        root.tag = "Monster";
        var sfx = root.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0.6f; sfx.minDistance = 2f; sfx.maxDistance = 30f;
        var voice = root.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0.6f; voice.minDistance = 2f; voice.maxDistance = 30f;
        var core = root.AddComponent<CapsuleCollider>(); core.center = new Vector3(0f, 1.25f, 0f); core.radius = 0.24f; core.height = 1.3f;

        // Model under RiseOffset (MonsterEntrance moves it up through the floor; the Animator lives here).
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.name = "RiseOffset";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
        var animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        smr.sharedMesh = BuildWeakPointMesh(smr, out int weakTris);
        smr.sharedMaterials = new[] { BodyMat(1001), BodyMat(1002), WeakMat() };
        smr.updateWhenOffscreen = true; // rises from below the floor; never cull mid-entrance

        // Bone-following body hit volumes, fitted at model scale 1 (then scaled with the bones).
        log.Append(MonsterHitVolumeBuilder.Build(root, 0.85f));

        // Weak point: right forearm (the lead axe arm: it opens every swing toward the player).
        var wpT = Bone(root, "RightForeArm").Find("HitVol_R_LowerArm");
        var wp = wpT.gameObject.AddComponent<MonsterWeakPoint>();
        wp.damageMultiplier = 1.75f; wp.hitClip = Sfx("L3_WeakPoint_Hit"); wp.glowRenderer = smr; wp.glowMaterialIndex = 2;
        log.AppendLine($"Weak point: HitVol_R_LowerArm (x{wp.damageMultiplier}), glowing submesh 2 = {weakTris} forearm tris.");

        // Axes, one per hand, each with its own clash colliders and fire emitter.
        var axeR = AttachAxe(Bone(root, "RightHand"), true);
        var axeL = AttachAxe(Bone(root, "LeftHand"), false);
        // Burning axes: a small Shuriken flame + spark emitter on each blade (MonsterAxeFlames drives them).
        var flameR = AddAxeFlame(axeR, out var sparksR); var flameL = AddAxeFlame(axeL, out var sparksL);

        // Chest: weapon-fire light (both axes are in front of it) and the enrage aura + body embers.
        var chest = Bone(root, "Spine2");
        var fireLightT = Child(chest, "WeaponFireLight", new Vector3(0f, 0.05f, 0.45f));
        var fireLight = fireLightT.gameObject.AddComponent<Light>(); fireLight.type = LightType.Point; fireLight.color = new Color(1f, 0.45f, 0.12f); fireLight.range = 3.5f; fireLight.intensity = 0f; fireLight.shadows = LightShadows.None;
        var auraT = Child(chest, "EnrageAura", new Vector3(0f, 0f, 0.8f)); // MonsterGlow's light: far enough out to spill on the dock, not flood the skin
        var aura = auraT.gameObject.AddComponent<Light>(); aura.type = LightType.Point; aura.color = new Color(1f, 0.15f, 0.08f); aura.range = 4f; aura.intensity = 0f; aura.shadows = LightShadows.None;
        var embers = Level3Builder.Particles(chest, "EnrageEmbers", new Vector3(0f, -0.1f, 0f), L3Mat("M_L3_Ember"), new Color(1f, 0.3f, 0.1f), 45f, 1.2f, 0.05f, 0.6f, 0.9f, ParticleSystemShapeType.Box, new Vector3(0.55f, 0.7f, 0.35f));

        model.transform.localScale = Vector3.one * ModelScale;

        // Summoning circle (detached to the ground at runtime), sickly green.
        var circle = Child(root.transform, "SummoningCircle", new Vector3(0f, 0.02f, 0f));
        circle.localScale = Vector3.one * 1.8f;
        var cr = circle.gameObject.AddComponent<MeshRenderer>(); circle.gameObject.AddComponent<MeshFilter>().sharedMesh = Level3Builder.GroundQuad(); cr.sharedMaterial = L3Mat("M_L3_RuneCircle");
        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var rune = circle.gameObject.AddComponent<GroundRuneVFX>(); rune.runeRenderer = cr; rune.color = new Color(0.35f, 1f, 0.25f, 1f); rune.spinSpeed = 30f;
        var circleParticles = Level3Builder.Particles(circle, "SummonEmbers", Vector3.zero, L3Mat("M_L3_Ember"), new Color(0.45f, 1f, 0.3f), 60f, 1.8f, 0.05f, 0.9f, 1.2f, ParticleSystemShapeType.Circle, Vector3.one);
        var csh = circleParticles.shape; csh.radius = 0.85f; csh.rotation = new Vector3(-90f, 0f, 0f);
        rune.particles = circleParticles;
        var clT = Child(circle, "SummonLight", new Vector3(0f, 0.6f, 0f)); var cl = clT.gameObject.AddComponent<Light>(); cl.type = LightType.Point; cl.color = new Color(0.35f, 1f, 0.3f); cl.range = 6f; cl.intensity = 0f; cl.shadows = LightShadows.None;
        rune.glowLight = cl; rune.glowLightIntensity = 3f;

        // Danger zone for the unblockable slam (deep red), detached in Awake.
        var dz = Child(root.transform, "DangerZone", Vector3.zero);
        var ring = Child(dz, "Ring", Vector3.zero);
        ring.gameObject.AddComponent<MeshFilter>().sharedMesh = Level3Builder.GroundQuad();
        var rr = ring.gameObject.AddComponent<MeshRenderer>(); rr.sharedMaterial = L3Mat("M_L3_DangerRing"); rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var ringVfx = ring.gameObject.AddComponent<GroundRuneVFX>(); ringVfx.runeRenderer = rr; ringVfx.color = new Color(1f, 0.08f, 0.04f, 1f); ringVfx.spinSpeed = -60f; ringVfx.pulseSpeed = 4f;
        var fill = Child(dz, "Fill", new Vector3(0f, 0.005f, 0f));
        fill.gameObject.AddComponent<MeshFilter>().sharedMesh = Level3Builder.GroundQuad();
        var fr = fill.gameObject.AddComponent<MeshRenderer>(); fr.sharedMaterial = L3Mat("M_L3_DangerFill"); fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var dzi = dz.gameObject.AddComponent<DangerZoneIndicator>(); dzi.ring = ringVfx; dzi.fill = fill; dzi.fillRenderer = fr;

        // Slam impact burst (dust + splinters), placed by MonsterAI at the impact point.
        var burst = Level3Builder.Particles(root.transform, "SlamImpactBurst", Vector3.zero, L3Mat("M_L3_Dust"), new Color(0.55f, 0.5f, 0.45f, 0.8f), 0f, 0.9f, 0.35f, 2.5f, 1.2f, ParticleSystemShapeType.Circle, Vector3.one);
        var bm = burst.main; bm.loop = false; bm.playOnAwake = false;
        var be = burst.emission; be.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
        var bs = burst.shape; bs.radius = 0.6f; bs.rotation = new Vector3(-90f, 0f, 0f);

        // --- Components ---
        var health = root.AddComponent<MonsterHealth>(); health.maxHealth = 1500f; health.currentHealth = 1500f;
        var ctrl = root.AddComponent<MonsterController>();
        ctrl.monsterName = "Twin-Axe Brute"; ctrl.audioSource = voice;
        ctrl.deathSound = ClipAt("Assets/Audio/SFX/Monster/universfield-dramatic-death-collapse-352720.mp3");
        ctrl.weaponType = WeaponType.DualAxe;
        var anim = root.AddComponent<MonsterAnimator>(); anim.animator = animator; anim.fallbackRenderer = smr;
        var ai = root.AddComponent<MonsterAI>();
        ai.useHeadTarget = true; ai.thinkInterval = 0.15f; ai.stopDistance = 1.45f; ai.facePlayer = true; ai.canMove = true;
        ai.sfxAudioSource = sfx; ai.telegraphRenderer = smr;
        ai.extraTintRenderers = new Renderer[] { axeR.GetComponentInChildren<MeshRenderer>(), axeL.GetComponentInChildren<MeshRenderer>() };
        ai.heavyPityCycles = 4; ai.desperationHpFraction = 0.5f; ai.lowHpCooldownScale = 0.85f; ai.swingSfxLead = 0.12f;
        ai.allowParry = true; ai.parryDamageMultiplier = 0.35f;
        ai.dangerZone = dzi; ai.impactBurstVfx = burst;
        ai.weaponFireVfx = null; ai.extraWeaponFireVfx = new ParticleSystem[0]; ai.weaponFireLight = fireLight; ai.weaponFireLightIntensity = 2.5f; // the flames are MonsterAxeFlames'; fire rows still light the dock
        ai.canDefend = true; ai.defendCooldown = 8.0f; ai.defendDuration = 1.0f; ai.defendDamageMultiplier = 0.3f; // timed guard every ~8 s (5.6-10.4 with jitter): most guards are now reactive
        // Reactive guard (rolled per incoming swing while the brute is free) + a jittered timed guard. Phase 2 reads you a
        // little better; enraged it trades defence for aggression (its offence is already the escalation).
        ai.defendCooldownJitter = 0.3f;
        ai.reactiveDefendChance = 0.40f; ai.reactiveDefendChancePhase2 = 0.50f; ai.reactiveDefendChanceEnraged = 0.30f;
        ai.reactiveSwingSpeed = 2.5f; ai.reactiveRange = 1.25f; ai.reactiveCooldown = 3f; ai.reactiveDefendDuration = 0.8f;
        ai.reactiveTarget = chest;
        ai.defendTintEnabled = true; ai.defendTintColor = new Color(0.45f, 0.65f, 1f);
        ai.defendBraceClip = ClipAt("Assets/Audio/SFX/Level2/L2_Defend_Brace.wav"); ai.defendBlockClip = ClipAt("Assets/Audio/SFX/Level2/L2_Defend_Block.wav");

        var entrance = root.AddComponent<MonsterEntrance>();
        entrance.riseOffset = model.transform; entrance.startDepth = 2.9f; entrance.summoningCircle = rune;
        entrance.riseStateClip = "L3_Rise"; entrance.roarStateClip = "L3_Roar";
        entrance.audioSource = sfx; entrance.summonClip = Sfx("L3_Summon_Circle"); entrance.riseClip = Sfx("L3_Rise_Grind"); entrance.roarClip = Sfx("L3_Roar_Entrance");

        // Body glow (emission mask): breathing pulse, wind-up surge, escalation to white-hot; owns the chest light
        // (the old enrage aura), so BossPhaseController no longer drives it.
        var glow = root.AddComponent<MonsterGlow>();
        glow.bodyRenderer = smr; glow.materialIndices = new[] { 0, 1 }; glow.animator = animator; glow.glowLight = aura;
        glow.calmColor = BodyGlow;
        glow.glowLightCalm = 0.12f; glow.glowLightEnraged = 1.8f; // at rest the skin keeps its own maroon; enraged it burns red
        wp.hitStopSeconds = 0.09f; wp.hapticAmplitude = 0.8f; wp.hapticSeconds = 0.12f; wp.bodyGlow = glow;
        // Phase-shift power-up ripple: a ground ring quad (radius 1 at scale 1) that MonsterGlow grows and fades; off until a phase break.
        var rippleT = Child(root.transform, "PowerUpRipple", new Vector3(0f, 0.035f, 0f));
        rippleT.gameObject.AddComponent<MeshFilter>().sharedMesh = Level3Builder.GroundQuad();
        var rippleR = rippleT.gameObject.AddComponent<MeshRenderer>(); rippleR.sharedMaterial = L3Mat("M_L3_RuneCircle");
        rippleR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rippleR.receiveShadows = false; rippleR.enabled = false;
        glow.rippleRenderer = rippleR;

        // Thrown axes: burning, trailing, whirring in 3D, caught back on the clip's catch frame.
        var thrower = root.AddComponent<MonsterAxeThrow>();
        thrower.axes = new[] { axeR.transform, axeL.transform };
        thrower.axeFire = new ParticleSystem[2]; // a thrown axe keeps its own flame (MonsterAxeFlames roars it in flight)
        thrower.trails = new[] { AddAxeTrail(axeR), AddAxeTrail(axeL) };
        thrower.spinSources = new[] { AddSpinSource(axeR), AddSpinSource(axeL) };
        thrower.catchClip = Sfx("L3_AxeCatch"); thrower.catchSource = sfx;
        ai.axeThrow = thrower;

        var flames = root.AddComponent<MonsterAxeFlames>();
        flames.flames = new[] { flameR, flameL }; flames.sparks = new[] { sparksR, sparksL };

        // Serpent of light: 3 orbs with short blue trails coiling feet -> head around the live skeleton.
        var auraRoot = Child(root.transform, "SerpentAura", Vector3.zero);
        var serpent = root.AddComponent<MonsterSerpentAura>();
        var orbs = new TrailRenderer[3];
        for (int i = 0; i < orbs.Length; i++) orbs[i] = AddAuraOrb(auraRoot, i);
        serpent.orbs = orbs;
        serpent.leftFoot = Bone(root, "LeftFoot"); serpent.rightFoot = Bone(root, "RightFoot"); serpent.hips = Bone(root, "Hips");
        serpent.leftUpLeg = Bone(root, "LeftUpLeg"); serpent.rightUpLeg = Bone(root, "RightUpLeg"); serpent.chest = chest;
        serpent.leftArm = Bone(root, "LeftArm"); serpent.rightArm = Bone(root, "RightArm"); serpent.neck = Bone(root, "Neck"); serpent.head = Bone(root, "Head");

        // Final-boss kill: slow motion + final-blow boom (BattleManager.victoryPanelDelay holds the panel).
        var finale = root.AddComponent<BossFinale>();
        finale.audioSource = voice; finale.finalBlowClip = Sfx("L3_FinalBlow");

        var phases = root.AddComponent<BossPhaseController>();
        phases.enrageAura = null; phases.enrageEmbers = embers;
        phases.enrageRoarClip = Sfx("L3_Roar_Enrage"); phases.phaseChangeClip = Sfx("L3_PhaseChange_Growl");
        phases.enrageRoarSeconds = 1.8f;

        // Shared fight tuning (personal-space clamp, MonsterDefense guards / guard break / poise, single-hit cap,
        // phase-shift pauses, enrage tempo): one source for all three monsters, so a rebuild keeps it.
        CombatTuning.ConfigureMonster(root, 3);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        EditorSceneManager.ClosePreviewScene(buildScene);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        log.AppendLine($"Prefab {PrefabPath} built.");
        return log.ToString();
    }

    static TrailRenderer AddAxeTrail(GameObject axe)
    {
        var edge = axe.transform.Find("BladeEdge");
        var tr = edge.gameObject.AddComponent<TrailRenderer>();
        tr.sharedMaterial = TrailMat();
        tr.time = 0.22f; tr.minVertexDistance = 0.04f;
        tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.22f), new Keyframe(1f, 0f));
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.55f, 0.2f), 0f), new GradientColorKey(new Color(0.85f, 0.07f, 0.03f), 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false;
        tr.emitting = false;
        return tr;
    }

    static Material TrailMat()
    {
        string path = $"{MatDir}/M_L3_AxeTrail.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        // Same setup as the summoning circle: URP Particles/Unlit, transparent additive, vertex colour from the trail.
        m = new Material(L3Mat("M_L3_RuneCircle")) { name = "M_L3_AxeTrail" };
        m.SetTexture("_BaseMap", null);
        m.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static AudioSource AddSpinSource(GameObject axe)
    {
        var src = axe.AddComponent<AudioSource>();
        src.clip = Sfx("L3_AxeThrow_Spin"); src.loop = true; src.playOnAwake = false; src.volume = 0.9f;
        src.spatialBlend = 1f; src.minDistance = 1f; src.maxDistance = 20f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 0.6f;
        return src;
    }

    /// <summary>
    /// Quest-budget flame on the blade (Shuriken, additive soft glow, world-space particles with a short life so a
    /// swing leaves only a brief flicker): ~10 particles alive per axe at rest, ~15 Standard, ~24 Heavy (~30 enraged). Plus a spark
    /// emitter (rate 0) that MonsterAxeFlames bursts when an attack lands.
    /// </summary>
    static ParticleSystem AddAxeFlame(GameObject axe, out ParticleSystem sparks)
    {
        var edge = axe.transform.Find("BladeEdge");
        var go = new GameObject("AxeFlame");
        go.transform.SetParent(edge, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.07f); // middle of the blade (edge 0.08..0.28 out, BladeEdge at 0.26)
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1f; main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.26f, 0.34f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape; // sizes in metres; only the emitter box follows the 1.25 model scale
        main.maxParticles = 40;
        main.gravityModifier = -0.08f;
        var em = ps.emission; em.rateOverTime = 20f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.04f, 0.62f, 0.14f); // along the blade
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0.475f); vel.z = new ParticleSystem.MinMaxCurve(0f); // all three constants (mixed curve modes warn every frame)
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.6f; noise.scrollSpeed = 0.8f;
        noise.quality = ParticleSystemNoiseQuality.Low; noise.octaveCount = 1;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.88f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0.25f),
                          new GradientColorKey(new Color(1f, 0.22f, 0.04f), 0.6f), new GradientColorKey(new Color(0.55f, 0.04f, 0.02f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.12f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/VFX/Level2/M_L2_FireAdditive.mat");
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sortMode = ParticleSystemSortMode.None;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.maxParticleSize = 0.5f;

        // Sparks: burst outward from the blade, fall, stretched by speed.
        var sgo = new GameObject("AxeSparks");
        sgo.transform.SetParent(edge, false);
        sparks = sgo.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var sm = sparks.main;
        sm.loop = false; sm.playOnAwake = false; sm.duration = 1f;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.4f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
        sm.simulationSpace = ParticleSystemSimulationSpace.World; sm.scalingMode = ParticleSystemScalingMode.Shape;
        sm.maxParticles = 40; sm.gravityModifier = 0.8f;
        var se = sparks.emission; se.rateOverTime = 0f;
        var ssh = sparks.shape; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = 0.06f;
        var scol = sparks.colorOverLifetime; scol.enabled = true;
        var sg = new Gradient();
        sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        scol.color = sg;
        var sr = sgo.GetComponent<ParticleSystemRenderer>();
        sr.sharedMaterial = L3Mat("M_L3_Ember");
        sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.035f; sr.lengthScale = 1f;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;
        return ps;
    }

    const string AuraTexPath = "Assets/VFX/Level3/T_L3_AuraTrail.png";

    /// <summary>Electric-blue trail: texture across the trail width = white-hot core with blue falloff (additive).</summary>
    static Material AuraMat()
    {
        const int W = 16, H = 64; // u along the trail, v across it
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H - 0.5f;
            float core = Mathf.Exp(-(v / 0.10f) * (v / 0.10f));
            float glow = Mathf.Exp(-(v / 0.30f) * (v / 0.30f));
            var c = Color.Lerp(new Color(0.15f, 0.45f, 1f), Color.white, core) * glow;
            for (int x = 0; x < W; x++) px[y * W + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(glow * 255));
        }
        tex.SetPixels32(px); tex.Apply();
        System.IO.File.WriteAllBytes(AuraTexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(AuraTexPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AuraTexPath);
        ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.SaveAndReimport();

        string path = $"{MatDir}/M_L3_AuraTrail.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(L3Mat("M_L3_RuneCircle")) { name = "M_L3_AuraTrail" }; AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AuraTexPath)); // additive, like the rune circle
        m.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(m);
        return m;
    }

    static TrailRenderer AddAuraOrb(Transform parent, int i)
    {
        var go = new GameObject("AuraOrb_" + i);
        go.transform.SetParent(parent, false);
        var tr = go.AddComponent<TrailRenderer>();
        tr.sharedMaterial = AuraMat();
        tr.time = 0.5f; tr.minVertexDistance = 0.035f; tr.numCapVertices = 0; tr.numCornerVertices = 0;
        tr.alignment = LineAlignment.View; tr.textureMode = LineTextureMode.Stretch;
        tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.25f, 0.8f), new Keyframe(1f, 0f));
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.25f, 0.55f, 1f), 0.3f), new GradientColorKey(new Color(0.05f, 0.15f, 1f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.3f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false;
        tr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; tr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        tr.emitting = false;
        return tr;
    }

    // ======================================================================= measured attack table
    /// <summary>Samples every attack clip on an instance of the built prefab and records the hit frames.</summary>
    public static string MeasureHits()
    {
        var log = new System.Text.StringBuilder();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            // Evaluated through the real Animator (applyRootMotion off), exactly as at runtime. AnimationClip.SampleAnimation
            // is not used: on a child Animator it bakes the clip's root travel into the body.
            var animator = inst.transform.Find("RiseOffset").GetComponent<Animator>();
            animator.Rebind();
            var edgeR = Bone(inst, "RightHand").Find("MonsterAxe_R/BladeEdge");
            var edgeL = Bone(inst, "LeftHand").Find("MonsterAxe_L/BladeEdge");
            const float dt = 1f / 120f;
            foreach (var a in AttackClips)
            {
                var clip = Clip(a.state);
                int n = Mathf.CeilToInt(clip.length / dt) + 1;
                var pr = new Vector3[n]; var pl = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    animator.Play(a.state, 0, Mathf.Min(1f, i * dt / clip.length)); animator.Update(0f);
                    pr[i] = inst.transform.InverseTransformPoint(edgeR.position); pl[i] = inst.transform.InverseTransformPoint(edgeL.position);
                }
                a.hits = new float[a.windows.Length];
                var where = new System.Text.StringBuilder();
                for (int w = 0; w < a.windows.Length; w++)
                {
                    int i0 = Mathf.Max(1, Mathf.FloorToInt(a.windows[w].x / dt)), i1 = Mathf.Min(n - 2, Mathf.CeilToInt(a.windows[w].y / dt));
                    int best = i0;
                    if (a.groundContact)
                    {
                        float minY = float.MaxValue;
                        for (int i = i0; i <= i1; i++) minY = Mathf.Min(minY, Mathf.Min(pr[i].y, pl[i].y));
                        for (int i = i0; i <= i1; i++) if (Mathf.Min(pr[i].y, pl[i].y) <= minY + 0.03f) { best = i; break; }
                    }
                    else if (a.frontMost)
                    {
                        float bestZ = float.MinValue;
                        for (int i = i0; i <= i1; i++) { float z = Mathf.Max(pr[i].z, pl[i].z); if (z > bestZ) { bestZ = z; best = i; } }
                    }
                    else
                    {
                        float bestV = -1f;
                        for (int i = i0; i <= i1; i++)
                        {
                            float v = Mathf.Max((pr[i + 1] - pr[i - 1]).magnitude, (pl[i + 1] - pl[i - 1]).magnitude) / (2f * dt);
                            if (v > bestV) { bestV = v; best = i; }
                        }
                    }
                    a.hits[w] = best * dt;
                    where.Append($" hit{w + 1} {a.hits[w]:F3}s R({pr[best].x:+0.00;-0.00},{pr[best].y:0.00},{pr[best].z:+0.00;-0.00}) L({pl[best].x:+0.00;-0.00},{pl[best].y:0.00},{pl[best].z:+0.00;-0.00})");
                }
                log.AppendLine($"  {a.state} ({clip.length:F2} s):{where}");
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        return log.ToString();
    }

    static AttackClip Spec(string state) => AttackClips.Find(a => a.state == state);

    public static List<MonsterAI.AttackDefinition> BuildAttackTable()
    {
        var standardCue = Sfx("L3_Telegraph_Standard"); var heavyCue = Sfx("L3_Telegraph_Heavy");
        var single = Sfx("L3_DualAxe_Swing_Single"); var dbl = Sfx("L3_DualAxe_Swing_Double"); var heavy = Sfx("L3_DualAxe_Swing_Heavy");
        Color std = new Color(1f, 0.3f, 0.2f), big = new Color(1f, 0.1f, 0.08f), dodge = new Color(0.75f, 0.2f, 1f);

        MonsterAI.AttackDefinition Row(string name, MonsterAI.AttackCategory cat, string state, float dmg, float arc, float wFull, float wLow, float wEnraged, float cd, Color tint, AudioClip cue, AudioClip swing)
        {
            var s = Spec(state);
            var follow = new float[s.hits.Length - 1];
            for (int i = 1; i < s.hits.Length; i++) follow[i - 1] = s.hits[i] - s.hits[0];
            float lastHit = s.hits[s.hits.Length - 1];
            return new MonsterAI.AttackDefinition
            {
                name = name, category = cat, animatorTrigger = state, animatorState = state,
                windUp = s.hits[0], followUpHits = follow, recover = Mathf.Max(0.3f, s.length - lastHit - 0.15f),
                damageMultiplier = dmg, hitArc = arc, weightFullHp = wFull, weightLowHp = wLow, enragedWeight = wEnraged, cooldown = cd,
                telegraphColor = tint, windUpClip = cue, windUpVolume = 0.75f, swingClip = swing, swingVolume = 0.9f,
            };
        }
        var table = new List<MonsterAI.AttackDefinition>
        {
            Row("Alternating Chops", MonsterAI.AttackCategory.Standard, "Atk_AltChops", 0.85f, 55f, 28f, 16f, 12f, 0f, std, standardCue, single),
            Row("Crossing Double Swing", MonsterAI.AttackCategory.Standard, "Atk_CrossSwing", 1.0f, 75f, 26f, 15f, 12f, 0f, std, standardCue, dbl),
            Row("Twin Diagonal Hack", MonsterAI.AttackCategory.Standard, "Atk_TwinHack", 1.05f, 50f, 24f, 14f, 10f, 0f, std, standardCue, dbl),
            Row("Reaping Flurry", MonsterAI.AttackCategory.Combo, "Atk_ReapFlurry", 0.7f, 60f, 10f, 16f, 14f, 4f, std, standardCue, single),
            Row("Twin Whirlwind", MonsterAI.AttackCategory.Heavy, "Atk_TwinWhirl", 1.5f, 180f, 6f, 16f, 14f, 4f, big, heavyCue, heavy),
            Row("Twin Overhead Cleave", MonsterAI.AttackCategory.Heavy, "Atk_TwinCleave", 1.8f, 40f, 5f, 14f, 12f, 5f, big, heavyCue, heavy),
            Row("Leaping Double Cleave", MonsterAI.AttackCategory.Heavy, "Atk_LeapCleave", 1.7f, 45f, 0f, 12f, 12f, 6f, big, heavyCue, heavy),
            Row("Earthsplitter Slam", MonsterAI.AttackCategory.Special, "Atk_GroundSlam", 2.0f, 180f, 1.5f, 6f, 34f, 6f, dodge, null, heavy),
            Row("Rending Double Strike", MonsterAI.AttackCategory.Combo, "Atk_RendStrike", 0.8f, 55f, 10f, 14f, 12f, 3f, std, standardCue, single),
            Row("Spinning Reap", MonsterAI.AttackCategory.Standard, "Atk_SpinReap", 1.0f, 90f, 14f, 10f, 8f, 0f, std, standardCue, dbl),
            Row("Backhand Double Chop", MonsterAI.AttackCategory.Standard, "Atk_BackChop", 0.85f, 50f, 16f, 10f, 8f, 0f, std, standardCue, single),
            // Ranged rows: only while the player is out of melee reach (MonsterAI.ChooseRanged), weighted among themselves.
            Row("Hurled Axe", MonsterAI.AttackCategory.Standard, "Atk_AxeThrow", 1.3f, 0f, 10f, 8f, 5f, 5f, std, standardCue, single),
            Row("Twin Hurl", MonsterAI.AttackCategory.Combo, "Atk_TwinHurl", 1.0f, 0f, 0f, 9f, 10f, 6f, std, standardCue, single),
            Row("Brute Rush", MonsterAI.AttackCategory.Heavy, "Atk_Rush", 1.5f, 70f, 8f, 10f, 10f, 4f, big, heavyCue, dbl),
            Row("Twin Cyclone", MonsterAI.AttackCategory.Heavy, "Atk_TwinCyclone", 1.4f, 180f, 0f, 12f, 12f, 7f, big, heavyCue, heavy),
            Row("Twin Overhead Chain", MonsterAI.AttackCategory.Combo, "Atk_OverheadChain", 1.1f, 40f, 0f, 12f, 12f, 6f, big, heavyCue, heavy),
            Row("Lunge Strike", MonsterAI.AttackCategory.Heavy, "Atk_LungeStrike", 1.4f, 70f, 8f, 9f, 9f, 5f, big, heavyCue, dbl),
        };
        table[0].followUpArcs = new[] { 55f };
        table[3].followUpArcs = new[] { 60f, 70f };
        table[8].followUpArcs = new[] { 55f };
        table[10].followUpArcs = new[] { 50f };
        table[5].fireVfx = true;
        table[6].fireVfx = true; table[6].maxHpFraction = 0.66f; // phase 2+
        var slam = table[7];
        slam.unblockable = true; slam.fireVfx = true; slam.dodgeRadius = 1.5f; // shockwave: ~0.65 m back or ~1.4 m sideways from the 1.45 m stand-off
        slam.warningClip = Sfx("L3_Slam_Warning"); slam.warningVolume = 1f;
        slam.impactClip = Sfx("L3_Slam_Impact"); slam.impactVolume = 1f;

        var hurl = table[11];
        hurl.rangedKind = MonsterAI.RangedKind.Throw; hurl.minRange = 2.4f; hurl.maxRange = 6.5f;
        hurl.throwAxes = new[] { 0 }; hurl.catchTime = Spec("Atk_AxeThrow").catchTime;
        var twin = table[12];
        twin.rangedKind = MonsterAI.RangedKind.Throw; twin.minRange = 2.4f; twin.maxRange = 6.5f; twin.maxHpFraction = 0.5f;
        twin.throwAxes = new[] { 0, 1 }; twin.catchTime = Spec("Atk_TwinHurl").catchTime;
        foreach (var t in new[] { hurl, twin })
        {
            // Hit frames are releases; the monster is free once the catch pose has eased back to idle.
            t.recover = Mathf.Max(0.3f, Spec(t.animatorState).length - t.catchTime - 0.1f) ;
            t.followUpArcs = new float[0];
        }
        table[14].maxHpFraction = 0.66f;                  // Twin Cyclone: phase 2+
        table[14].followUpArcs = new[] { 180f };
        table[15].maxHpFraction = 0.66f; table[15].fireVfx = true;   // Twin Overhead Chain: phase 2+
        table[15].followUpArcs = new[] { 40f };
        var lungeRow = table[16];
        lungeRow.rangedKind = MonsterAI.RangedKind.Rush; lungeRow.minRange = 1.8f; lungeRow.maxRange = 3.0f;
        lungeRow.travelWindow = Spec("Atk_LungeStrike").travel;
        var rush = table[13];
        rush.rangedKind = MonsterAI.RangedKind.Rush; rush.minRange = 2.6f; rush.maxRange = 5.5f;
        rush.travelWindow = Spec("Atk_Rush").travel;
        return table;
    }

    /// <summary>Writes the measured table onto the prefab; slam impact point = measured blade contact distance.</summary>
    static string ApplyAttackTable()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var ai = root.GetComponent<MonsterAI>();
            ai.attackTable = BuildAttackTable();
            // Impact centre = where the blades meet the dock, measured (forward metres from the root).
            ai.attackTable[7].impactForward = slamContactForward;
            ai.impactBurstOffset = new Vector2(slamContactForward, 0.05f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            return $"Attack table: {ai.attackTable.Count} attacks written; slam impact centre {slamContactForward:F2} m forward, radius {ai.attackTable[7].dodgeRadius} m.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static float slamContactForward = 1.0f;

    static void MeasureSlamContact()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            var animator = inst.transform.Find("RiseOffset").GetComponent<Animator>();
            animator.Rebind();
            var s = Spec("Atk_GroundSlam");
            animator.Play(s.state, 0, s.hits[0] / s.length); animator.Update(0f);
            var r = inst.transform.InverseTransformPoint(Bone(inst, "RightHand").Find("MonsterAxe_R/BladeEdge").position);
            var l = inst.transform.InverseTransformPoint(Bone(inst, "LeftHand").Find("MonsterAxe_L/BladeEdge").position);
            slamContactForward = Mathf.Max(0.6f, (r.z + l.z) * 0.5f);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // ======================================================================= entry point
    [MenuItem("Tools/Level 3/Build Dual-Axe Monster")]
    public static void BuildMenu() { Debug.Log(BuildAll()); }

    public static string BuildAll()
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine(BuildMaterials(true));
        log.AppendLine(BuildAxeMesh());
        log.AppendLine("Clips:"); log.Append(BuildClips());
        log.AppendLine(BuildController());
        log.Append(BuildPrefab());
        log.AppendLine("Measured hit frames:"); log.Append(MeasureHits());
        MeasureSlamContact();
        log.AppendLine(ApplyAttackTable());
        return log.ToString();
    }
}
