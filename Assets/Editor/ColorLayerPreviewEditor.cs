// ColorLayerPreviewEditor.cs
// Purpose: Inspector buttons of ColorLayerPreview: fire every colour layer of the compositor next to it, in Play mode (the layers
//          are timed on game time, so they decay and pulse exactly as in a fight).
// Dependencies: ColorLayerPreview, MonsterColorCompositor, PlayerWeaponGlow, StrikeFit.ColorFx
// Attach to: nothing (custom inspector)

using StrikeFit.ColorFx;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ColorLayerPreview))]
public class ColorLayerPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var pv = (ColorLayerPreview)target;
        var m = pv.Monster;
        var w = pv.Weapon;
        ColorCompositor comp = m != null ? (ColorCompositor)m : w;
        if (comp == null) { EditorGUILayout.HelpBox("Needs a MonsterColorCompositor or a PlayerWeaponGlow on this object.", MessageType.Warning); return; }
        if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play mode to fire layers (they run on game time).", MessageType.Info); return; }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Fire a layer", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Hit Tap")) Hit(comp, SwingTierFx.Tap);
            if (GUILayout.Button("Light")) Hit(comp, SwingTierFx.Light);
            if (GUILayout.Button("Medium")) Hit(comp, SwingTierFx.Medium);
            if (GUILayout.Button("Full")) Hit(comp, SwingTierFx.Full);
            if (m != null && GUILayout.Button("Weak pt")) Hit(comp, SwingTierFx.WeakPoint);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clash")) Clash(comp, false);
            if (GUILayout.Button("Parry")) Clash(comp, true);
            if (GUILayout.Button("Strike")) { comp.SetLayer(ColorLayer.Strike, new Color(1f, 0.2f, 0.15f), 0.7f, 0.2f, LayerCurve.Decay); Pump(); }
        }
        if (m != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Telegraph")) { m.Telegraph(pv.telegraphColor, pv.telegraphSeconds); Pump(pv.telegraphSeconds + 0.2f); }
                if (GUILayout.Button("Telegraph + Full hit")) { m.Telegraph(pv.telegraphColor, pv.telegraphSeconds); Hit(comp, SwingTierFx.Full); Pump(pv.telegraphSeconds + 0.2f); }
                if (GUILayout.Button("Cue")) { m.Cue(new Color(0.78f, 0.9f, 1f), 0.15f); Pump(); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                m.previewGuard = GUILayout.Toggle(m.previewGuard, "Guard", "Button");
                if (GUILayout.Button("Wound from slider")) { m.previewHpFraction = pv.hpFraction; Pump(); }
                if (GUILayout.Button("Real HP")) { m.previewHpFraction = -1f; Pump(); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rage from slider")) { m.SetRage(pv.rage, pv.enrageTint); Pump(); }
                if (GUILayout.Button("Death")) { var p = m.palette; m.Stack.Set(ColorLayer.Death, p.deathColor, Color.black, p.deathWeight, 0f, p.deathFadeSeconds, LayerCurve.FadeIn, ColorGroup.All, Time.time, 0f, p.deathDelay); Pump(p.deathDelay + p.deathFadeSeconds + 0.2f); }
                if (GUILayout.Button("Clear all")) { for (int i = 0; i < (int)ColorLayer.Count; i++) comp.ClearLayer((ColorLayer)i); m.previewGuard = false; m.previewHpFraction = -1f; Pump(); }
            }
        }
        EditorGUILayout.HelpBox("Writes so far: " + comp.Writes + "   slots: " + comp.TargetCount + "   without emission keyword: " + comp.SlotsWithoutEmission, MessageType.None);
    }

    void Hit(ColorCompositor comp, SwingTierFx tier)
    {
        var m = comp as MonsterColorCompositor;
        if (m != null && m.palette != null)
        {
            m.palette.Tier(tier, out Color c, out float i, out float s);
            m.Stack.Set(ColorLayer.Hit, c, c, m.palette.hitWeight, i, s, LayerCurve.Decay, ColorGroup.AllBody, Now());
        }
        var w = comp as PlayerWeaponGlow;
        if (w != null && w.profile != null)
        {
            w.profile.Tier(tier, out Color c, out float i, out float s);
            w.Stack.Set(ColorLayer.Hit, c, c, w.profile.hitWeight, i, s, LayerCurve.Decay, ColorGroup.AllWeapon, Now());
        }
        Pump();
    }

    void Clash(ColorCompositor comp, bool parry)
    {
        var m = comp as MonsterColorCompositor;
        if (m != null) { if (parry) m.NotifyParry(); else { var p = m.palette; m.Stack.Set(ColorLayer.Clash, p.clashColor, p.clashColor, p.clashWeight, p.clashIntensity, p.clashSeconds, LayerCurve.Decay, ColorGroup.AllWeapon, Now()); m.Stack.Set(ColorLayer.ClashRim, p.clashColor, p.clashColor, 0.15f, p.rimIntensity, p.clashSeconds, LayerCurve.Decay, ColorGroup.AllBody, Now()); } }
        var w = comp as PlayerWeaponGlow;
        if (w != null && w.profile != null) { var c = parry ? w.profile.parryColor : w.profile.clashColor; w.Stack.Set(ColorLayer.Clash, c, c, w.profile.clashWeight, w.profile.clashIntensity, parry ? w.profile.parrySeconds : w.profile.clashSeconds, LayerCurve.Decay, ColorGroup.AllWeapon, Now()); }
        Pump();
    }

    static float Now() => Time.time;

    static void Pump(float seconds = 0.6f) { }   // Play mode: the compositor refreshes itself every frame

    public override bool RequiresConstantRepaint() => Application.isPlaying;
}
