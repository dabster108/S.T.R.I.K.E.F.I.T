// ColorCompositor.cs
// Purpose: The ONE writer of the MaterialPropertyBlock of the renderer slots it owns (a monster's body, armour and weapons, or one
//          player weapon). Other scripts never touch these blocks: they describe what happened through the layer API (SetLayer /
//          ClearLayer, or the named helpers of MonsterColorCompositor / PlayerWeaponGlow), and this component combines every layer
//          in a fixed blend order (ColorStack) once per frame, after every other script (DefaultExecutionOrder 10000).
//          Writing rules:
//            - _BaseColor / _Color = the material's own colour x the group's blended tint (white = the material as authored);
//            - _EmissionColor = the material's own emission (per slot, optional) + the group's summed, clamped layer emission;
//            - each group is evaluated once per frame, and its slots are written only when the group's result changed (one
//              SetPropertyBlock per slot then, none while nothing moves);
//            - renderers, material colours and shader IDs are cached in Awake (ShaderIds); nothing allocates per frame.
//          Emission only shows on materials with the _EMISSION keyword enabled; Awake counts the slots without it
//          (SlotsWithoutEmission) so a missing keyword is visible in the Editor.
// Dependencies: StrikeFit.ColorFx (ColorStack), ShaderIds
// Attach to: nothing directly (MonsterColorCompositor / PlayerWeaponGlow derive from it)

using StrikeFit.ColorFx;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public abstract class ColorCompositor : MonoBehaviour
{
    [System.Serializable]
    public struct Slot
    {
        public Renderer renderer;
        [Tooltip("Material index, or -1 for every material of the renderer.")]
        public int materialIndex;
        public ColorGroup group;
        [Tooltip("Add the material's own emission under the layers (off for slots whose emission a script drives fully).")]
        public bool addMaterialEmission;
    }

    [Tooltip("Every renderer slot this component owns. It is the only code that writes their MaterialPropertyBlock.")]
    public Slot[] slots = new Slot[0];

    public ColorStack Stack { get; } = new ColorStack();
    /// <summary>MaterialPropertyBlock writes so far (tests, diagnostics).</summary>
    public int Writes { get; private set; }
    /// <summary>Slots whose material has no _EMISSION keyword: base colour only there.</summary>
    public int SlotsWithoutEmission { get; private set; }
    public int TargetCount => targets != null ? targets.Length : 0;

    struct Target
    {
        public Renderer renderer;
        public int index;
        public int group;          // index into GroupBits
        public Color matBase, matEmission;
        public bool addMatEmission;
    }

    static readonly ColorGroup[] GroupBits = { ColorGroup.Body, ColorGroup.Accent, ColorGroup.Glow, ColorGroup.WeakPoint, ColorGroup.Weapon, ColorGroup.WeaponAccent };

    Target[] targets;
    MaterialPropertyBlock mpb;
    readonly bool[] groupUsed = new bool[6];
    readonly GroupResult[] last = new GroupResult[6];
    readonly bool[] haveLast = new bool[6];
    int seenVersion = -1;
    bool built;

    protected virtual void Awake() => Build();

    /// <summary>The resting look (BaseFor) changed without a layer change (a cosmetic palette): re-evaluate every group next frame.</summary>
    protected void Invalidate()
    {
        seenVersion = -1;
        for (int g = 0; g < haveLast.Length; g++) haveLast[g] = false;
    }

    /// <summary>Caches the slots (also called again by tools after the slot list changed).</summary>
    public void Build()
    {
        mpb = mpb ?? new MaterialPropertyBlock();
        int n = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var r = slots[i].renderer;
            if (r == null) continue;
            n += slots[i].materialIndex < 0 ? r.sharedMaterials.Length : 1;
        }
        targets = new Target[n];
        for (int g = 0; g < groupUsed.Length; g++) { groupUsed[g] = false; haveLast[g] = false; }
        int k = 0; SlotsWithoutEmission = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (s.renderer == null) continue;
            var mats = s.renderer.sharedMaterials;
            int from = s.materialIndex < 0 ? 0 : s.materialIndex, to = s.materialIndex < 0 ? mats.Length : s.materialIndex + 1;
            int gi = GroupIndex(s.group);
            groupUsed[gi] = true;
            for (int m = from; m < to; m++)
            {
                var mat = m < mats.Length ? mats[m] : null;
                Color b = Color.white, e = Color.black;
                bool emissive = false;
                if (mat != null)
                {
                    if (mat.HasProperty(ShaderIds.BaseColor)) b = mat.GetColor(ShaderIds.BaseColor);
                    else if (mat.HasProperty(ShaderIds.Color)) b = mat.GetColor(ShaderIds.Color);
                    emissive = mat.HasProperty(ShaderIds.EmissionColor) && mat.IsKeywordEnabled("_EMISSION");
                    if (emissive) e = mat.GetColor(ShaderIds.EmissionColor);
                }
                if (!emissive) SlotsWithoutEmission++;
                targets[k++] = new Target { renderer = s.renderer, index = m, group = gi, matBase = b, matEmission = e, addMatEmission = s.addMaterialEmission };
            }
        }
        System.Array.Resize(ref targets, k);
        seenVersion = -1;
        built = true;
    }

    /// <summary>A script replaced the renderer that draws a slot (WeaponSurfaceProxy draws the Level 1 hammers through a runtime
    /// copy and hides the original): the slot follows the new renderer.</summary>
    public void ReplaceRenderer(Renderer from, Renderer to)
    {
        if (from == null || to == null) return;
        bool changed = false;
        for (int i = 0; i < slots.Length; i++) if (slots[i].renderer == from) { slots[i].renderer = to; changed = true; }
        if (changed) Build();
    }

    static int GroupIndex(ColorGroup g)
    {
        for (int i = 0; i < GroupBits.Length; i++) if (g == GroupBits[i]) return i;
        return 0;
    }

    /// <summary>The group's resting tint and emission (palette).</summary>
    protected abstract void BaseFor(ColorGroup group, out Color tint, out Color emission);

    /// <summary>Base-colour weight and group mask a layer uses when it is set through SetLayer.</summary>
    protected abstract float DefaultWeight(ColorLayer id);
    protected abstract ColorGroup DefaultMask(ColorLayer id);

    /// <summary>Called once per frame before the layers are combined (held layers that follow a source: wound, guard...).</summary>
    protected virtual void Tick(float now) { }

    // ───────────────────────── the layer API ─────────────────────────

    /// <summary>Starts a layer with its default weight and groups. duration <= 0 holds it until ClearLayer.</summary>
    public void SetLayer(ColorLayer id, Color color, float intensity, float duration, LayerCurve curve)
    {
        Stack.Set(id, color, color, DefaultWeight(id), intensity, duration, curve, DefaultMask(id), Time.time, curve == LayerCurve.Pulse ? 3f : 0f);
    }

    public void ClearLayer(ColorLayer id) => Stack.Clear(id);

    // ───────────────────────── the writer ─────────────────────────

    protected virtual void LateUpdate() => Refresh(Time.time);

    /// <summary>Combines the layers and writes the slots whose group changed (public for tests and the preview tool).</summary>
    public void Refresh(float now)
    {
        if (!built) Build();
        Tick(now);
        Stack.Expire(now);
        if (!Stack.Animating(now) && Stack.Version == seenVersion) return;
        seenVersion = Stack.Version;
        for (int g = 0; g < GroupBits.Length; g++)
        {
            if (!groupUsed[g]) continue;
            BaseFor(GroupBits[g], out Color tint, out Color emission);
            var res = Stack.Evaluate(GroupBits[g], tint, emission, now);
            if (haveLast[g] && res.Approximately(last[g])) continue;
            last[g] = res; haveLast[g] = true;
            Write(g, res);
        }
    }

    void Write(int group, in GroupResult res)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            ref var t = ref targets[i];
            if (t.group != group || t.renderer == null) continue;
            var b = new Color(t.matBase.r * res.baseTint.r, t.matBase.g * res.baseTint.g, t.matBase.b * res.baseTint.b, t.matBase.a);
            var e = res.emission;
            if (t.addMatEmission && !res.suppressMaterialEmission) e = new Color(e.r + t.matEmission.r, e.g + t.matEmission.g, e.b + t.matEmission.b, 1f);
            mpb.Clear();
            mpb.SetColor(ShaderIds.BaseColor, b);
            mpb.SetColor(ShaderIds.Color, b);
            mpb.SetColor(ShaderIds.EmissionColor, e);
            t.renderer.SetPropertyBlock(mpb, t.index);
            Writes++;
        }
    }

    /// <summary>The last result written for a group (tests, preview).</summary>
    public bool TryGetGroupResult(ColorGroup group, out GroupResult result)
    {
        int g = GroupIndex(group);
        result = last[g];
        return haveLast[g];
    }
}
