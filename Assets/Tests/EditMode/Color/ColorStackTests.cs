// ColorStackTests.cs
// Purpose: EditMode tests of the colour compositor: layer priority and blend order, the emission clamp, flash decay timing, the
//          telegraph staying readable over every other layer, determinism (call order never matters), no allocation in the update
//          path, the writer only writing when something changed, and the prefab wiring (every monster and player weapon prefab has
//          its compositor, a palette / profile and slots).
// Dependencies: StrikeFit.ColorFx (ColorStack, palettes), NUnit; MonsterColorCompositor / PlayerWeaponGlow through reflection
// Attach to: nothing (Test Runner, EditMode)

using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace StrikeFit.ColorFx.Tests
{
    public class ColorStackTests
    {
        static readonly Color Red = new Color(1f, 0.1f, 0.1f), Blue = new Color(0.4f, 0.6f, 1f), Yellow = new Color(1f, 0.9f, 0.2f);

        static float MaxChannel(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        static float Dist(Color a, Color b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        static int Allocations(Action action)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try { action(); }
            finally { recorder.enabled = false; recorder.CollectFromAllThreads(); }
            return recorder.sampleBlockCount;
        }

        // ───────────────────────── blend order and priority ─────────────────────────
        [Test]
        public void LaterLayersWinTheBaseColour_InFixedBlendOrder()
        {
            var s = new ColorStack();
            s.Set(ColorLayer.Guard, Blue, Blue, 1f, 0f, 0f, LayerCurve.Hold, ColorGroup.Body, 0f);
            s.Set(ColorLayer.Hit, Yellow, Yellow, 1f, 0f, 0f, LayerCurve.Hold, ColorGroup.Body, 0f);
            var r = s.Evaluate(ColorGroup.Body, Color.white, Color.black, 0.1f);
            Assert.Less(Dist(r.baseTint, Yellow), 1e-4f, "Hit comes after Guard in the blend order, so at full weight it wins the base colour");

            var half = new ColorStack();
            half.Set(ColorLayer.Guard, Blue, Blue, 1f, 0f, 0f, LayerCurve.Hold, ColorGroup.Body, 0f);
            half.Set(ColorLayer.Hit, Yellow, Yellow, 0.5f, 0f, 0f, LayerCurve.Hold, ColorGroup.Body, 0f);
            var h = half.Evaluate(ColorGroup.Body, Color.white, Color.black, 0.1f);
            var want = Color.Lerp(Blue, Yellow, 0.5f);
            Assert.Less(Dist(h.baseTint, want), 1e-4f, "base colours lerp by weight, lower layer first");
        }

        [Test]
        public void EmissionLayersAdd_AndOnlyTouchTheirGroups()
        {
            var s = new ColorStack();
            s.settings.maxEmission = 10f;
            s.Set(ColorLayer.Hit, Red, new Color(0.2f, 0f, 0f), 0f, 1f, 0f, LayerCurve.Hold, ColorGroup.AllBody, 0f);
            s.Set(ColorLayer.Clash, Yellow, new Color(0f, 0.3f, 0f), 0f, 1f, 0f, LayerCurve.Hold, ColorGroup.AllWeapon, 0f);
            s.Set(ColorLayer.Guard, Blue, new Color(0f, 0f, 0.1f), 0f, 1f, 0f, LayerCurve.Hold, ColorGroup.AllBody, 0f);
            var body = s.Evaluate(ColorGroup.Body, Color.white, new Color(0.05f, 0.05f, 0.05f), 0.1f);
            var weapon = s.Evaluate(ColorGroup.Weapon, Color.white, Color.black, 0.1f);
            Assert.AreEqual(0.25f, body.emission.r, 1e-4f); Assert.AreEqual(0.05f, body.emission.g, 1e-4f); Assert.AreEqual(0.15f, body.emission.b, 1e-4f);
            Assert.AreEqual(0f, weapon.emission.r, 1e-4f); Assert.AreEqual(0.3f, weapon.emission.g, 1e-4f);
        }

        // ───────────────────────── clamp ─────────────────────────
        [Test]
        public void EmissionIsClamped_KeepingTheHue()
        {
            var s = new ColorStack();
            s.settings = new BlendSettings { maxEmission = 1f, telegraphMinWeight = 0.5f, telegraphMinEmission = 0.3f, telegraphReserve = 0.4f };
            foreach (ColorLayer id in new[] { ColorLayer.Wound, ColorLayer.Guard, ColorLayer.Cue, ColorLayer.Hit, ColorLayer.ClashRim })
                s.Set(id, Yellow, Yellow, 0f, 2f, 0f, LayerCurve.Hold, ColorGroup.Body, 0f);
            var r = s.Evaluate(ColorGroup.Body, Color.white, Color.black, 0.1f);
            Assert.AreEqual(1f, MaxChannel(r.emission), 1e-4f, "five stacked layers never exceed the clamp");
            Assert.AreEqual(Yellow.g / Yellow.r, r.emission.g / r.emission.r, 1e-3f, "the clamp scales, it does not shift the hue toward white");
        }

        // ───────────────────────── flash decay ─────────────────────────
        [Test]
        public void HitFlashDecays_AndEndsOnTime()
        {
            var s = new ColorStack();
            s.Set(ColorLayer.Hit, Red, Red, 0.5f, 1f, 0.2f, LayerCurve.Decay, ColorGroup.Body, 10f);
            Assert.AreEqual(1f, s.Strength(ColorLayer.Hit, 10f), 1e-5f, "full at the hit");
            Assert.AreEqual(0.25f, s.Strength(ColorLayer.Hit, 10.1f), 1e-4f, "(1 - t/d)^2 at half time");
            Assert.Less(s.Strength(ColorLayer.Hit, 10.19f), 0.01f);
            Assert.AreEqual(0f, s.Strength(ColorLayer.Hit, 10.2f), 1e-6f, "gone at 0.2 s (float time: 10.2f - 10f is 0.1999998)");
            Assert.IsTrue(s.Animating(10.1f));
            s.Expire(10.21f);
            Assert.IsFalse(s.Get(ColorLayer.Hit).active, "expired layers switch off");
            Assert.IsFalse(s.Animating(10.21f));
        }

        [Test]
        public void PaletteTierFlashes_LastBetween015And025s_AndGrowWithTheTier()
        {
            var p = ScriptableObject.CreateInstance<MonsterColorPalette>();
            try
            {
                float lastIntensity = -1f;
                foreach (var t in new[] { SwingTierFx.Tap, SwingTierFx.Light, SwingTierFx.Medium, SwingTierFx.Full })
                {
                    p.Tier(t, out Color c, out float i, out float sec);
                    Assert.That(sec, Is.InRange(0.15f, 0.25f), t.ToString());
                    Assert.Greater(i, lastIntensity, t + " is stronger than the tier below");
                    lastIntensity = i;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(p); }
        }

        // ───────────────────────── telegraph ─────────────────────────
        [Test]
        public void Telegraph_StaysReadable_OverEveryOtherLayer()
        {
            var settings = new BlendSettings { maxEmission = 1.4f, telegraphMinWeight = 0.55f, telegraphMinEmission = 0.3f, telegraphReserve = 0.45f };
            var s = new ColorStack { settings = settings };
            for (int i = 0; i < (int)ColorLayer.Telegraph; i++)
                s.Set((ColorLayer)i, Yellow, Yellow, 1f, 3f, 0f, LayerCurve.Hold, ColorGroup.All, 0f);
            s.Set(ColorLayer.Telegraph, Red, Red, 0.85f, 0.55f, 2f, LayerCurve.Pulse, ColorGroup.All, 0f, 3f);
            // Sample the whole pulse, including its darkest point.
            for (float t = 0.01f; t < 1.9f; t += 0.05f)
            {
                var r = s.Evaluate(ColorGroup.Body, Color.white, Color.black, t);
                float redShare = r.emission.r - r.emission.g;   // the red the telegraph adds over the yellow pile
                Assert.GreaterOrEqual(redShare, settings.telegraphMinEmission - 1e-3f, "telegraph emission share at t=" + t);
                Assert.LessOrEqual(MaxChannel(r.emission), settings.maxEmission + 1e-4f, "still inside the clamp");
                Assert.LessOrEqual(Dist(r.baseTint, Red), (1f - settings.telegraphMinWeight) * Dist(Yellow, Red) + 1e-3f, "base is at least minWeight toward the telegraph");
            }
        }

        // ───────────────────────── determinism ─────────────────────────
        [Test]
        public void TheResultDoesNotDependOnTheOrderLayersWereSetIn()
        {
            var a = new ColorStack(); var b = new ColorStack();
            a.Set(ColorLayer.Hit, Yellow, Yellow, 0.5f, 0.8f, 0.2f, LayerCurve.Decay, ColorGroup.AllBody, 1f);
            a.Set(ColorLayer.Guard, Blue, Blue, 0.6f, 0.3f, 0f, LayerCurve.Hold, ColorGroup.AllBody, 1f);
            a.Set(ColorLayer.Telegraph, Red, Red, 0.85f, 0.5f, 1f, LayerCurve.Pulse, ColorGroup.All, 1f, 3f);
            b.Set(ColorLayer.Telegraph, Red, Red, 0.85f, 0.5f, 1f, LayerCurve.Pulse, ColorGroup.All, 1f, 3f);
            b.Set(ColorLayer.Guard, Blue, Blue, 0.6f, 0.3f, 0f, LayerCurve.Hold, ColorGroup.AllBody, 1f);
            b.Set(ColorLayer.Hit, Yellow, Yellow, 0.5f, 0.8f, 0.2f, LayerCurve.Decay, ColorGroup.AllBody, 1f);
            for (float t = 1f; t < 2.2f; t += 0.037f)
            {
                var ra = a.Evaluate(ColorGroup.Body, new Color(1f, 0.9f, 0.8f), new Color(0.1f, 0.05f, 0f), t);
                var rb = b.Evaluate(ColorGroup.Body, new Color(1f, 0.9f, 0.8f), new Color(0.1f, 0.05f, 0f), t);
                Assert.IsTrue(ra.Approximately(rb, 1e-6f), "t=" + t);
                var rc = a.Evaluate(ColorGroup.Body, new Color(1f, 0.9f, 0.8f), new Color(0.1f, 0.05f, 0f), t);
                Assert.IsTrue(ra.Approximately(rc, 0f), "evaluating twice gives exactly the same colour");
            }
        }

        [Test]
        public void HeldLayers_OnlyBumpTheVersion_WhenTheValueChanged()
        {
            var s = new ColorStack();
            s.SetHeld(ColorLayer.BodyGlow, Red, Red, 0f, 1f, ColorGroup.Glow, 0f);
            int v = s.Version;
            for (int i = 0; i < 10; i++) s.SetHeld(ColorLayer.BodyGlow, Red, Red, 0f, 1f, ColorGroup.Glow, i);
            Assert.AreEqual(v, s.Version, "the same glow every frame is not a change");
            s.SetHeld(ColorLayer.BodyGlow, Yellow, Yellow, 0f, 1f, ColorGroup.Glow, 11f);
            Assert.AreEqual(v + 1, s.Version);
        }

        [Test]
        public void DeathFades_AfterItsDelay()
        {
            var s = new ColorStack();
            s.Set(ColorLayer.Hit, Red, Red, 0f, 1f, 0f, LayerCurve.Hold, ColorGroup.Body, 0f);
            s.Set(ColorLayer.Death, new Color(0.1f, 0.1f, 0.1f), Color.black, 1f, 0f, 1f, LayerCurve.FadeIn, ColorGroup.All, 0f, 0f, 0.5f);
            Assert.Greater(s.Evaluate(ColorGroup.Body, Color.white, Color.black, 0.4f).emission.r, 0.9f, "the death flash plays first");
            Assert.Less(s.Evaluate(ColorGroup.Body, Color.white, Color.black, 1.6f).emission.r, 1e-4f, "then everything fades out");
        }

        // ───────────────────────── allocation ─────────────────────────
        [Test]
        public void TheUpdatePath_AllocatesNothing()
        {
            var s = new ColorStack();
            void Frame(float t)
            {
                s.Set(ColorLayer.Hit, Yellow, Yellow, 0.5f, 1f, 0.2f, LayerCurve.Decay, ColorGroup.AllBody, t);
                s.SetHeld(ColorLayer.BodyGlow, Red, Red * (0.5f + 0.5f * Mathf.Sin(t)), 0f, 1f, ColorGroup.Glow, t);
                s.SetHeld(ColorLayer.Wound, Red, Red, 0.3f, 0.2f, ColorGroup.AllBody, t);
                s.Set(ColorLayer.Telegraph, Red, Red, 0.85f, 0.5f, 1f, LayerCurve.Pulse, ColorGroup.All, t, 3f);
                s.Expire(t + 0.05f);
                s.Animating(t + 0.05f);
                for (int g = 0; g < 6; g++) s.Evaluate((ColorGroup)(1 << g), Color.white, Color.black, t + 0.05f);
            }
            for (int i = 0; i < 20; i++) Frame(i * 0.1f);   // warm-up
            Assert.IsTrue(Allocations(() => { var junk = new object[8]; }) > 0, "the allocation recorder works");
            Assert.AreEqual(0, Allocations(() => { for (int i = 0; i < 300; i++) Frame(2f + i / 72f); }));
        }

        // ───────────────────────── the writer (MonoBehaviour, through reflection) ─────────────────────────
        static (GameObject go, Component comp, Renderer r) MakeMonsterCompositor()
        {
            var type = Type.GetType("MonsterColorCompositor, Assembly-CSharp");
            Assert.IsNotNull(type);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var r = go.GetComponent<Renderer>();
            var comp = go.AddComponent(type);
            var slotType = Type.GetType("ColorCompositor+Slot, Assembly-CSharp");
            var slot = Activator.CreateInstance(slotType);
            slotType.GetField("renderer").SetValue(slot, r);
            slotType.GetField("materialIndex").SetValue(slot, -1);
            slotType.GetField("group").SetValue(slot, ColorGroup.Body);
            slotType.GetField("addMaterialEmission").SetValue(slot, true);
            var arr = Array.CreateInstance(slotType, 1); arr.SetValue(slot, 0);
            type.GetField("slots").SetValue(comp, arr);
            type.GetField("palette").SetValue(comp, ScriptableObject.CreateInstance<MonsterColorPalette>());
            type.GetMethod("Build").Invoke(comp, null);
            return (go, comp, r);
        }

        [Test]
        public void Writer_WritesOnlyWhenSomethingChanged_AndAllocatesNothing()
        {
            var (go, comp, r) = MakeMonsterCompositor();
            try
            {
                var type = comp.GetType();
                var refresh = type.GetMethod("Refresh");
                var stack = (ColorStack)type.GetProperty("Stack").GetValue(comp);
                Func<int> writes = () => (int)type.GetProperty("Writes").GetValue(comp);
                var tick = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), comp, refresh);   // typed: no boxing per call

                tick(0f);
                int w0 = writes();
                Assert.Greater(w0, 0, "the first refresh writes the resting palette");
                for (int i = 1; i <= 30; i++) tick(i / 72f);
                Assert.AreEqual(w0, writes(), "nothing changed: no property-block write");

                stack.Set(ColorLayer.Hit, Yellow, Yellow, 0.5f, 1f, 0.2f, LayerCurve.Decay, ColorGroup.AllBody, 1f);
                tick(1.01f);
                Assert.Greater(writes(), w0, "a hit writes");
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb, 0);
                Assert.Greater(mpb.GetColor("_EmissionColor").r, 0.3f, "the flash is in the block");

                for (int i = 0; i < 40; i++) tick(1.02f + i / 72f);   // decays, expires
                int wEnd = writes();
                for (int i = 0; i < 20; i++) tick(2f + i / 72f);
                Assert.AreEqual(wEnd, writes(), "after the flash ended: quiet again");

                // Allocation: a flashing, pulsing stack refreshed every frame (called through a typed delegate, so nothing is boxed).
                stack.Set(ColorLayer.Telegraph, Red, Red, 0.85f, 0.5f, 5f, LayerCurve.Pulse, ColorGroup.All, 3f, 3f);
                for (int i = 0; i < 10; i++) tick(3f + i / 72f);
                int allocs = Allocations(() => { for (int i = 0; i < 100; i++) tick(3.2f + i / 72f); });
                Assert.LessOrEqual(allocs, 0, "Refresh + property-block writes allocate nothing");
            }
            finally
            {
                var pal = comp.GetType().GetField("palette").GetValue(comp) as UnityEngine.Object;
                UnityEngine.Object.DestroyImmediate(go);
                if (pal != null) UnityEngine.Object.DestroyImmediate(pal);
            }
        }

        // ───────────────────────── prefab wiring ─────────────────────────
        [Test]
        public void EveryMonsterPrefab_HasItsCompositor_PaletteAndSlots()
        {
            string[] paths = { "Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab", "Assets/Prefabs/Monsters/Monster_Medium_VampireLust.prefab", "Assets/Prefabs/Monsters/Monster_Hard_TwinAxeBrute.prefab" };
            var palettes = new System.Collections.Generic.HashSet<UnityEngine.Object>();
            foreach (var p in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var comp = go.GetComponent("MonsterColorCompositor");
                Assert.IsNotNull(comp, p);
                var pal = comp.GetType().GetField("palette").GetValue(comp) as UnityEngine.Object;
                Assert.IsNotNull(pal, p + " palette");
                palettes.Add(pal);
                var slots = (Array)comp.GetType().GetField("slots").GetValue(comp);
                Assert.Greater(slots.Length, 1, p + " has body and weapon slots");
            }
            Assert.AreEqual(3, palettes.Count, "each level has its own palette");
        }

        [Test]
        public void EveryPlayerWeaponPrefab_HasAGlow_WithAProfile()
        {
            string[] paths = { "Assets/Prefabs/Weapons/Weapon_Hammer.prefab",
                "Assets/Prefabs/Weapons/Level3/PlayerMace_Left.prefab", "Assets/Prefabs/Weapons/Level3/PlayerMace_Right.prefab",
                "Assets/Prefabs/Weapons/Level3/PlayerFlail_Left.prefab", "Assets/Prefabs/Weapons/Level3/PlayerFlail_Right.prefab",
                "Assets/Prefabs/Weapons/Level3/PlayerKatana_Left.prefab", "Assets/Prefabs/Weapons/Level3/PlayerKatana_Right.prefab" };
            foreach (var p in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var glow = go.GetComponent("PlayerWeaponGlow");
                Assert.IsNotNull(glow, p);
                Assert.IsNotNull(glow.GetType().GetField("profile").GetValue(glow), p + " profile");
                Assert.Greater(((Array)glow.GetType().GetField("slots").GetValue(glow)).Length, 0, p + " slots");
            }
        }
    }
}
