using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Efek runtime yang tidak bisa dibawa FBX: denyut emission (gem, bintang, pita, tepi emas)
/// dan "glint" cahaya yang menyapu huruf dari kiri ke kanan.
/// Dipasang otomatis oleh Tools > Poker Text > Setup All.
/// </summary>
[DisallowMultipleComponent]
public class PokerTextFX : MonoBehaviour
{
    [Header("Glint sweep")]
    public float glintPeriod = 3.2f;
    public float glintWidth = 0.16f;
    public float glintIntensity = 2.4f;
    public Color glintColor = new Color(1f, 0.92f, 0.72f, 1f);

    [Header("Emission pulse")]
    public float pulseSpeed = 1.6f;
    [Range(0f, 1f)] public float pulseAmount = 0.45f;

    class Slot
    {
        public Renderer renderer;
        public int index;
        public Color baseEmission;
        public bool letter;
        public bool pulse;
        public float phase;
        public float nx;
    }

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    readonly List<Slot> slots = new List<Slot>();
    MaterialPropertyBlock block;

    void Start()
    {
        block = new MaterialPropertyBlock();
        var renderers = GetComponentsInChildren<Renderer>(true);

        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var r in renderers)
        {
            if (!IsLetter(r)) continue;
            float cx = r.bounds.center.x;
            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
        }
        float span = Mathf.Max(0.0001f, maxX - minX);

        int counter = 0;
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            bool letter = IsLetter(r);
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.name == "Gold_Dust") continue;
                var s = new Slot
                {
                    renderer = r,
                    index = i,
                    letter = letter,
                    baseEmission = m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.black,
                    pulse = m.name.Contains("Gold_Edge") || m.name.Contains("RoseGold") ||
                            m.name.StartsWith("Gem_") || m.name.StartsWith("Sparkle") ||
                            m.name.StartsWith("Ribbon") || m.name.Contains("Heart"),
                    phase = counter * 0.37f,
                    nx = letter ? (r.bounds.center.x - minX) / span : 0f
                };
                slots.Add(s);
            }
            counter++;
        }
    }

    static bool IsLetter(Renderer r)
    {
        var mats = r.sharedMaterials;
        return mats.Length == 2 && mats[0] != null && mats[0].name.EndsWith("_Face");
    }

    void Update()
    {
        float t = Time.time;
        float sweep = Mathf.Repeat(t / glintPeriod, 1.5f) - 0.25f;

        foreach (var s in slots)
        {
            if (s.renderer == null) continue;
            Color c = s.baseEmission;
            if (s.pulse)
                c *= 1f + pulseAmount * Mathf.Sin(t * pulseSpeed + s.phase);
            if (s.letter)
            {
                float d = (s.nx - sweep) / glintWidth;
                c += glintColor * (glintIntensity * Mathf.Exp(-d * d));
            }
            s.renderer.GetPropertyBlock(block, s.index);
            block.SetColor(EmissionId, c);
            s.renderer.SetPropertyBlock(block, s.index);
        }
    }
}
