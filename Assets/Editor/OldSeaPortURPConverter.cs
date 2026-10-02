// OldSeaPortURPConverter.cs
// Purpose: The Old Sea Port pack ships Built-in pipeline materials (Standard, Standard (Specular setup), Legacy
//          particles, Tree Creator, two custom surface shaders); under this project's URP they render magenta.
//          Converts every material under Assets/Old Sea Port in place to URP equivalents, carrying over textures,
//          colours, smoothness, emission and alpha cutout. Skybox and GUI shaders are left alone (URP supports them).
// Menu: Tools > Level 3 > Convert Old Sea Port Materials To URP

using UnityEditor;
using UnityEngine;

public static class OldSeaPortURPConverter
{
    const string Root = "Assets/Old Sea Port";

    [MenuItem("Tools/Level 3/Convert Old Sea Port Materials To URP")]
    public static void ConvertMenu() { Debug.Log(Convert()); }

    public static string Convert()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit");
        var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        int converted = 0, skipped = 0;
        var sb = new System.Text.StringBuilder();

        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;
            string s = m.shader.name;

            if (s.StartsWith("Universal Render Pipeline") || s.StartsWith("Skybox") || s.StartsWith("GUI")) { skipped++; continue; }

            Texture main = Tex(m, "_MainTex"); Color color = Col(m, "_Color", Color.white);
            Texture bump = Tex(m, "_BumpMap"); float bumpScale = Flt(m, "_BumpScale", 1f);
            Texture metalGloss = Tex(m, "_MetallicGlossMap"); Texture specGloss = Tex(m, "_SpecGlossMap");
            Texture occlusion = Tex(m, "_OcclusionMap"); Texture emission = Tex(m, "_EmissionMap");
            Color emissionColor = Col(m, "_EmissionColor", Color.black);
            Color specColor = Col(m, "_SpecColor", new Color(0.2f, 0.2f, 0.2f));
            float gloss = Flt(m, "_Glossiness", 0.4f), glossScale = Flt(m, "_GlossMapScale", 1f), metallic = Flt(m, "_Metallic", 0f);
            float cutoff = Flt(m, "_Cutoff", 0.5f), mode = Flt(m, "_Mode", 0f);
            Vector2 tiling = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            bool emissionOn = m.IsKeywordEnabled("_EMISSION") || emission != null || emissionColor.maxColorComponent > 0.01f;

            if (s.StartsWith("Legacy Shaders/Particles"))
            {
                bool additive = s.Contains("Additive");
                Color tint = Col(m, "_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                m.shader = particles;
                m.SetTexture("_BaseMap", main);
                m.SetColor("_BaseColor", tint * 2f);
                SetTransparent(m, additive);
            }
            else if (s.StartsWith("Nature/Tree Creator"))
            {
                bool leaves = s.Contains("Leaves");
                m.shader = simpleLit;
                m.SetTexture("_BaseMap", main); m.SetColor("_BaseColor", color);
                if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
                if (leaves) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", Mathf.Max(0.3f, cutoff)); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 0f); }
            }
            else if (s == "MFEP_Stream")
            {
                // Water stream: transparent glossy blue-grey with the stream detail normals.
                m.shader = lit;
                m.SetColor("_BaseColor", new Color(0.10f, 0.16f, 0.20f, 0.85f));
                m.SetFloat("_Smoothness", 0.92f); m.SetFloat("_Metallic", 0f);
                var n = AssetDatabase.LoadAssetAtPath<Texture>(Root + "/Water/Stream_Detail_Normals.png");
                if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetTextureScale("_BaseMap", new Vector2(6, 6)); }
                SetTransparent(m, false);
            }
            else
            {
                // Standard / Standard (Specular setup) / Custom surface shaders -> URP Lit
                bool specular = s.Contains("Specular");
                m.shader = lit;
                m.SetTexture("_BaseMap", main); m.SetColor("_BaseColor", color);
                m.SetTextureScale("_BaseMap", tiling); m.SetTextureOffset("_BaseMap", offset);
                if (bump != null) { m.SetTexture("_BumpMap", bump); m.SetFloat("_BumpScale", bumpScale); m.EnableKeyword("_NORMALMAP"); }
                if (occlusion != null) { m.SetTexture("_OcclusionMap", occlusion); m.EnableKeyword("_OCCLUSIONMAP"); }
                if (specular)
                {
                    m.SetFloat("_WorkflowMode", 0f); m.EnableKeyword("_SPECULAR_SETUP");
                    m.SetColor("_SpecColor", specColor);
                    if (specGloss != null) { m.SetTexture("_SpecGlossMap", specGloss); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", glossScale); }
                    else m.SetFloat("_Smoothness", gloss);
                }
                else
                {
                    m.SetFloat("_WorkflowMode", 1f);
                    m.SetFloat("_Metallic", metallic);
                    if (metalGloss != null) { m.SetTexture("_MetallicGlossMap", metalGloss); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", glossScale); }
                    else m.SetFloat("_Smoothness", gloss);
                }
                if (emissionOn && emissionColor.maxColorComponent > 0.01f)
                {
                    m.SetTexture("_EmissionMap", emission); m.SetColor("_EmissionColor", emissionColor);
                    m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if (mode == 1f) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", cutoff); m.EnableKeyword("_ALPHATEST_ON"); }
                else if (mode >= 2f) SetTransparent(m, false);
            }
            EditorUtility.SetDirty(m);
            converted++;
            sb.Append(System.IO.Path.GetFileNameWithoutExtension(path)).Append(" (").Append(s).Append("), ");
        }
        AssetDatabase.SaveAssets();
        return $"Old Sea Port: {converted} materials converted to URP, {skipped} already compatible. {sb}";
    }

    static void SetTransparent(Material m, bool additive)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
    static Color Col(Material m, string p, Color d) => m.HasProperty(p) ? m.GetColor(p) : d;
    static float Flt(Material m, string p, float d) => m.HasProperty(p) ? m.GetFloat(p) : d;
}
