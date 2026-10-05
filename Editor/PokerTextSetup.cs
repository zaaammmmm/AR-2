#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Setup otomatis Poker Text untuk URP (Unity 6).
/// Letakkan hasil export Blender di  Assets/PokerText/  (FBX + folder Textures),
/// lalu jalankan  Tools > Poker Text > Setup All.
/// </summary>
public static class PokerTextSetup
{
    const string Root = "Assets/PokerText";
    const string TexDir = Root + "/Textures";
    const string MatDir = Root + "/Materials";
    const string PrefabDir = Root + "/Prefabs";
    const string AnimDir = Root + "/Animations";
    const string EnvDir = Root + "/Environment";

    static readonly string[] Words = { "STRAIGHT", "FLUSH", "PAIR" };
    static readonly Vector3[] WordPos =
    {
        new Vector3(0, 4.6f, 0), new Vector3(0, 0, 0), new Vector3(0, -4.8f, 0)
    };

    [MenuItem("Tools/Poker Text/Setup All")]
    public static void SetupAll()
    {
        if (!Directory.Exists(TexDir))
        {
            Debug.LogError("[PokerText] Folder tidak ditemukan: " + TexDir +
                           "  -> salin hasil export Blender ke Assets/PokerText/");
            return;
        }
        AssetDatabase.Refresh();
        EnsureDir(MatDir); EnsureDir(PrefabDir); EnsureDir(AnimDir); EnsureDir(EnvDir);

        ImportTextures();
        var mats = CreateMaterials();
        if (mats == null) return;
        foreach (var w in Words) ConfigureFbx(Root + "/" + w + ".fbx", mats);
        if (File.Exists(Root + "/ALL_WORDS.fbx")) ConfigureFbx(Root + "/ALL_WORDS.fbx", mats);
        BuildScene();
        AssetDatabase.SaveAssets();
        Debug.Log("[PokerText] Selesai. Buka scene: " + Root + "/PokerTextDemo.unity");
    }

    static void EnsureDir(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }

    // ------------------------------------------------------------ textures
    static void ImportTextures()
    {
        foreach (var f in Directory.GetFiles(TexDir, "*.png"))
        {
            string p = f.Replace('\\', '/');
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            string n = Path.GetFileNameWithoutExtension(p);

            if (n.EndsWith("_N"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                if (n.EndsWith("_MS"))
                {
                    ti.sRGBTexture = false;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = false;
                }
                else
                {
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = n.StartsWith("T_Ribbon");
                }
            }
            bool clamp = n.StartsWith("T_Ribbon") || n.StartsWith("T_Sparkle");
            ti.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.SaveAndReimport();
        }
    }

    static Texture2D Tex(string n)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + n + ".png");
    }

    // ----------------------------------------------------------- materials
    enum Surf { Opaque, Alpha, Additive }

    static Material Make(Shader shader, string name, string texSet = null, float bump = 1f,
                         Color? baseColor = null, float metallic = 0f, float smooth = 0.5f,
                         Color? emission = null, float emissionI = 0f, string emissionTex = null,
                         Surf surf = Surf.Opaque, bool doubleSided = false)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;

        Color bc = baseColor ?? Color.white;
        m.SetColor("_BaseColor", bc);
        m.SetColor("_Color", bc);

        Texture2D albedo = null, ms = null, nrm = null;
        if (texSet != null) { albedo = Tex(texSet + "_A"); ms = Tex(texSet + "_MS"); nrm = Tex(texSet + "_N"); }
        if (albedo != null) { m.SetTexture("_BaseMap", albedo); m.SetTexture("_MainTex", albedo); }

        if (ms != null)
        {
            m.SetTexture("_MetallicGlossMap", ms);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
        }
        else
        {
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
        }
        if (nrm != null)
        {
            m.SetTexture("_BumpMap", nrm);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
        }

        // emission selalu aktif agar MaterialPropertyBlock (PokerTextFX) bisa memakainya
        m.EnableKeyword("_EMISSION");
        Color em = (emission ?? Color.black) * emissionI;
        m.SetColor("_EmissionColor", em);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (!string.IsNullOrEmpty(emissionTex))
        {
            var et = Tex(emissionTex);
            if (et != null) m.SetTexture("_EmissionMap", et);
        }

        m.SetFloat("_Cull", doubleSided ? 0f : 2f);
        if (surf == Surf.Opaque)
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_SrcBlend", 1f);
            m.SetFloat("_DstBlend", 0f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = -1;
            m.SetOverrideTag("RenderType", "Opaque");
        }
        else
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", surf == Surf.Additive ? 2f : 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", 5f);                         // SrcAlpha
            m.SetFloat("_DstBlend", surf == Surf.Additive ? 1f : 10f); // One / OneMinusSrcAlpha
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", surf == Surf.Additive ? 1f : 10f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Dictionary<string, Material> CreateMaterials()
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            Debug.LogError("[PokerText] Shader URP/Lit tidak ditemukan. Pastikan project memakai URP.");
            return null;
        }
        var gold = new Color(1f, 0.55f, 0.10f);
        var goldE = new Color(1f, 0.65f, 0.15f);
        var rose = new Color(0.9f, 0.4f, 0.35f);
        var M = new Dictionary<string, Material>();
        void Add(Material m) { M[m.name] = m; }

        Add(Make(sh, "Gold_Face", "T_GoldFace", 1.0f, emission: gold, emissionI: 0.35f));
        Add(Make(sh, "Gold_Edge", "T_GoldEdge", 0.6f, emission: goldE, emissionI: 0.55f));
        Add(Make(sh, "Purple_Face", "T_PurpleFace", 0.6f));
        Add(Make(sh, "Silver_Face", "T_SilverFace", 0.5f));
        Add(Make(sh, "RoseGold_Edge", "T_RoseGold", 0.6f, emission: rose, emissionI: 0.25f));
        Add(Make(sh, "Gold_Heart", "T_GoldEdge", 0.5f, emission: goldE, emissionI: 0.3f));
        Add(Make(sh, "Rose_Heart", "T_RoseGold", 0.5f, emission: rose, emissionI: 0.2f));

        void Gem(string n, Color c) =>
            Add(Make(sh, n, null, 1f, new Color(c.r, c.g, c.b, 0.85f), 0f, 0.95f,
                     c, 2.2f, null, Surf.Alpha, true));
        Gem("Gem_Ruby", new Color(0.95f, 0.02f, 0.10f));
        Gem("Gem_Sapphire", new Color(0.10f, 0.25f, 1f));
        Gem("Gem_Amethyst", new Color(0.62f, 0.15f, 0.95f));

        Add(Make(sh, "Diamond", null, 1f, new Color(1f, 1f, 1f, 0.35f), 0.1f, 1f,
                 Color.white, 0.25f, null, Surf.Alpha, true));
        Add(Make(sh, "Sparkle_White", null, 1f, Color.black, 0f, 0.5f,
                 Color.white, 7f, "T_Sparkle_E", Surf.Opaque, true));
        Add(Make(sh, "Sparkle_Gold", null, 1f, Color.black, 0f, 0.5f,
                 new Color(1f, 0.8f, 0.3f), 7f, "T_Sparkle_E", Surf.Opaque, true));
        Add(Make(sh, "Gold_Dust", null, 1f, new Color(1f, 0.8f, 0.3f), 0f, 0.5f,
                 new Color(1f, 0.75f, 0.2f), 5f));

        var rp = Make(sh, "Ribbon_Purple", "T_RibbonPurple", 1f, Color.white, 0f, 0.4f,
                      new Color(0.75f, 0.25f, 1f), 1.2f, "T_RibbonPurple", Surf.Additive, true);
        var rk = Make(sh, "Ribbon_Pink", "T_RibbonPink", 1f, Color.white, 0f, 0.4f,
                      new Color(1f, 0.6f, 0.9f), 1.5f, "T_RibbonPink", Surf.Additive, true);
        // Ribbon: texture RGBA di slot _BaseMap (albedo di folder dipakai langsung)
        foreach (var r in new[] { rp, rk })
        {
            var t = Tex(r.name == "Ribbon_Purple" ? "T_RibbonPurple" : "T_RibbonPink");
            if (t != null) { r.SetTexture("_BaseMap", t); r.SetTexture("_MainTex", t); }
        }
        Add(rp); Add(rk);
        AssetDatabase.SaveAssets();
        return M;
    }

    // ----------------------------------------------------------------- FBX
    static void ConfigureFbx(string path, Dictionary<string, Material> mats)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) { Debug.LogWarning("[PokerText] FBX tidak ditemukan: " + path); return; }

        imp.globalScale = 1f;
        imp.useFileScale = true;
        imp.importCameras = false;
        imp.importLights = false;
        imp.importBlendShapes = true;
        imp.bakeAxisConversion = true;
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
        foreach (var kv in mats)
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
        imp.SaveAndReimport();

        var clips = imp.defaultClipAnimations;
        if (clips != null && clips.Length > 0)
        {
            foreach (var c in clips) { c.loopTime = true; c.loopPose = true; }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }
    }

    // --------------------------------------------------------------- scene
    static void BuildScene()
    {
        var cube = BuildReflectionCubemap();
        var profile = BuildVolumeProfile();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.28f, 0.28f, 0.32f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = cube;
        RenderSettings.reflectionIntensity = 1.3f;

        // kamera
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.45f, 0.45f, 0.48f);
        cam.fieldOfView = 39.6f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 100f;
        camGo.transform.position = new Vector3(0, 0, -21f);
        camGo.transform.rotation = Quaternion.identity;
        var data = camGo.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;

        // cahaya
        MakeLight("Key Light", new Vector3(50, 30, 0), 2.2f, new Color(1f, 0.95f, 0.85f));
        MakeLight("Fill Light", new Vector3(30, -140, 0), 0.8f, new Color(0.8f, 0.75f, 1f));

        // volume (bloom + tonemapping)
        var volGo = new GameObject("Global Volume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        // prefab per kata + instance di scene
        for (int i = 0; i < Words.Length; i++)
        {
            string fbxPath = Root + "/" + Words[i] + ".fbx";
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) continue;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();

            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null)
            {
                string cp = AnimDir + "/" + Words[i] + ".controller";
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(cp);
                if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(cp, clip);
                anim.runtimeAnimatorController = ctrl;
            }
            if (inst.GetComponent<PokerTextFX>() == null) inst.AddComponent<PokerTextFX>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + "/" + Words[i] + ".prefab");
            Object.DestroyImmediate(inst);

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            placed.transform.position = WordPos[i];
        }

        EditorSceneManager.SaveScene(scene, Root + "/PokerTextDemo.unity");
    }

    static void MakeLight(string name, Vector3 euler, float intensity, Color color)
    {
        var go = new GameObject(name);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = intensity;
        l.color = color;
        go.transform.rotation = Quaternion.Euler(euler);
    }

    // ----------------------------------------------- bloom + tonemapping
    static VolumeProfile BuildVolumeProfile()
    {
        string path = EnvDir + "/PokerTextVolume.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var bloom = profile.Add<Bloom>();
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(1.1f);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(new Color(1f, 0.92f, 0.85f));
        AssetDatabase.AddObjectToAsset(bloom, profile);

        var tm = profile.Add<Tonemapping>();
        tm.mode.Override(TonemappingMode.ACES);
        AssetDatabase.AddObjectToAsset(tm, profile);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    // --------------------------------- studio reflection (untuk logam)
    static Cubemap BuildReflectionCubemap()
    {
        const int N = 128;
        var cm = new Cubemap(N, TextureFormat.RGBAHalf, true);
        var faces = new[]
        {
            CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY,
            CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ
        };
        var px = new Color[N * N];
        foreach (var face in faces)
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f;
                    float v = (y + 0.5f) / N * 2f - 1f;
                    px[y * N + x] = Studio(FaceDir(face, u, v).normalized);
                }
            cm.SetPixels(px, face);
        }
        cm.Apply(true);

        string path = EnvDir + "/StudioReflection.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(cm, path);
        return cm;
    }

    static Vector3 FaceDir(CubemapFace f, float u, float v)
    {
        switch (f)
        {
            case CubemapFace.PositiveX: return new Vector3(1, -v, -u);
            case CubemapFace.NegativeX: return new Vector3(-1, -v, u);
            case CubemapFace.PositiveY: return new Vector3(u, 1, v);
            case CubemapFace.NegativeY: return new Vector3(u, -1, -v);
            case CubemapFace.PositiveZ: return new Vector3(u, -v, 1);
            default: return new Vector3(-u, -v, -1);
        }
    }

    static Color Studio(Vector3 d)
    {
        float s = 0.5f + 0.5f * Mathf.Sin(d.y * 9f + d.x * 2.5f + 0.7f);
        float box = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.9f, s)) * 3.2f;
        float key = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, new Vector3(-0.4f, 0.55f, -0.7f).normalized)), 12f) * 4f;
        float fill = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, new Vector3(0.8f, 0.2f, -0.3f).normalized)), 8f) * 2f;
        var warm = new Color(1f, 0.95f, 0.88f) * (0.12f + box + key);
        var cool = new Color(0.7f, 0.65f, 1f) * fill;
        var c = warm + cool;
        c.a = 1f;
        return c;
    }
}
#endif
