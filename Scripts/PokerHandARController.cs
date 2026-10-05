using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Image tracking poker (AR Foundation 6 / Unity 6).
/// - Satu kartu terdeteksi  -> model 3D kartu itu muncul di atas gambarnya.
/// - Semua kartu satu grup terdeteksi BERSAMAAN -> kartu grup itu disembunyikan,
///   teks poker (FLUSH / PAIR / STRAIGHT) muncul di titik tengah kartu-kartu tersebut.
/// Script ini tidak mengubah komponen XR Origin / ARTrackedImageManager yang sudah ada.
/// </summary>
public enum PokerHandGroup { None, Flush, Pair, Straight }

[Serializable]
public class PokerCardEntry
{
    [Tooltip("Nama gambar PERSIS seperti di ReferenceImageLibrary.")]
    public string referenceImageName;
    public GameObject cardPrefab;
    public PokerHandGroup group = PokerHandGroup.None;

    [Header("Offset model terhadap gambar")]
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localEulerAngles = Vector3.zero;
    public Vector3 localScale = Vector3.one;
}

public class PokerHandARController : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] ARTrackedImageManager trackedImageManager;
    [SerializeField] Camera arCamera;
    [Tooltip("Naikkan Max Number Of Moving Images otomatis agar cukup untuk satu grup.")]
    [SerializeField] bool autoRaiseMaxMovingImages = true;

    [Header("Kartu (isi 12 entri)")]
    [SerializeField] List<PokerCardEntry> cards = new List<PokerCardEntry>();

    [Header("Teks poker (prefab)")]
    [SerializeField] GameObject flushPrefab;
    [SerializeField] GameObject pairPrefab;
    [SerializeField] GameObject straightPrefab;
    [Tooltip("Teks asli lebarnya +-8 unit; kecilkan agar pas di atas kartu.")]
    [SerializeField] float textScale = 0.03f;
    [SerializeField] float textHeightOffset = 0.06f;
    [Tooltip("Teks selalu menghadap kamera. Matikan untuk mengikuti arah gambar.")]
    [SerializeField] bool faceCamera = true;
    [Tooltip("Aktifkan jika teks tampak terbalik/cermin.")]
    [SerializeField] bool flipFacing = false;
    [SerializeField] float followSmoothing = 12f;
    [SerializeField] bool debugLog = false;

    class TrackedCard
    {
        public ARTrackedImage image;
        public PokerCardEntry entry;
        public GameObject instance;
    }

    readonly Dictionary<string, PokerCardEntry> entryByName = new Dictionary<string, PokerCardEntry>();
    readonly Dictionary<PokerHandGroup, List<PokerCardEntry>> groupEntries =
        new Dictionary<PokerHandGroup, List<PokerCardEntry>>();
    readonly Dictionary<PokerHandGroup, bool> groupComplete = new Dictionary<PokerHandGroup, bool>();
    readonly Dictionary<PokerHandGroup, GameObject> texts = new Dictionary<PokerHandGroup, GameObject>();
    readonly Dictionary<TrackableId, TrackedCard> tracked = new Dictionary<TrackableId, TrackedCard>();
    readonly HashSet<PokerCardEntry> trackingNow = new HashSet<PokerCardEntry>();

    void Awake()
    {
        if (trackedImageManager == null)
            trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
        if (arCamera == null) arCamera = Camera.main;

        foreach (var e in cards)
        {
            if (e == null || string.IsNullOrEmpty(e.referenceImageName)) continue;
            if (entryByName.ContainsKey(e.referenceImageName))
            {
                Debug.LogWarning("[PokerHandAR] Nama gambar ganda: " + e.referenceImageName);
                continue;
            }
            entryByName[e.referenceImageName] = e;

            if (e.group == PokerHandGroup.None) continue;
            if (!groupEntries.TryGetValue(e.group, out var list))
                groupEntries[e.group] = list = new List<PokerCardEntry>();
            list.Add(e);
        }

        CreateText(PokerHandGroup.Flush, flushPrefab);
        CreateText(PokerHandGroup.Pair, pairPrefab);
        CreateText(PokerHandGroup.Straight, straightPrefab);
    }

    void CreateText(PokerHandGroup g, GameObject prefab)
    {
        if (prefab == null) return;
        var go = Instantiate(prefab);
        go.name = "PokerText_" + g;
        go.transform.localScale = Vector3.one * textScale;
        go.SetActive(false);
        texts[g] = go;
    }

    void Start()
    {
        if (!autoRaiseMaxMovingImages || trackedImageManager == null) return;
        int need = 1;
        foreach (var kv in groupEntries) need = Mathf.Max(need, kv.Value.Count);
        if (trackedImageManager.requestedMaxNumberOfMovingImages < need)
            trackedImageManager.requestedMaxNumberOfMovingImages = need;
    }

    void OnEnable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
    }

    void OnDisable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
    }

    void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (var img in args.added) Register(img);
        foreach (var img in args.updated)
            if (!tracked.ContainsKey(img.trackableId)) Register(img);
        foreach (var kv in args.removed) tracked.Remove(kv.Key);
    }

    void Register(ARTrackedImage img)
    {
        string n = img.referenceImage.name;
        if (!entryByName.TryGetValue(n, out var entry))
        {
            if (debugLog) Debug.LogWarning("[PokerHandAR] Gambar tidak ada di daftar kartu: " + n);
            return;
        }

        var t = new TrackedCard { image = img, entry = entry };
        if (entry.cardPrefab != null)
        {
            var go = Instantiate(entry.cardPrefab, img.transform);
            go.transform.localPosition = entry.localPosition;
            go.transform.localRotation = Quaternion.Euler(entry.localEulerAngles);
            go.transform.localScale = entry.localScale;
            go.SetActive(false);
            t.instance = go;
        }
        tracked[img.trackableId] = t;
        if (debugLog) Debug.Log("[PokerHandAR] Terdaftar: " + n);
    }

    void LateUpdate()
    {
        trackingNow.Clear();
        foreach (var t in tracked.Values)
            if (t.image != null && t.image.trackingState == TrackingState.Tracking)
                trackingNow.Add(t.entry);

        foreach (var kv in groupEntries)
        {
            bool complete = kv.Value.Count > 0;
            foreach (var e in kv.Value)
                if (!trackingNow.Contains(e)) { complete = false; break; }
            groupComplete[kv.Key] = complete;
            UpdateText(kv.Key, complete);
        }

        foreach (var t in tracked.Values)
        {
            if (t.instance == null) continue;
            bool isTracking = t.image != null && t.image.trackingState == TrackingState.Tracking;
            bool hiddenByGroup = t.entry.group != PokerHandGroup.None &&
                                 groupComplete.TryGetValue(t.entry.group, out var c) && c;
            bool show = isTracking && !hiddenByGroup;
            if (t.instance.activeSelf != show) t.instance.SetActive(show);
        }
    }

    void UpdateText(PokerHandGroup g, bool complete)
    {
        if (!texts.TryGetValue(g, out var go) || go == null) return;

        if (!complete)
        {
            if (go.activeSelf) go.SetActive(false);
            return;
        }

        Vector3 sum = Vector3.zero;
        int count = 0;
        Transform first = null;
        foreach (var t in tracked.Values)
        {
            if (t.entry.group != g || t.image == null ||
                t.image.trackingState != TrackingState.Tracking) continue;
            sum += t.image.transform.position;
            count++;
            if (first == null) first = t.image.transform;
        }
        if (count == 0) return;

        Vector3 pos = sum / count + Vector3.up * textHeightOffset;
        Quaternion rot = TargetRotation(pos, first);

        bool snap = !go.activeSelf;
        if (snap) go.SetActive(true);

        if (snap || followSmoothing <= 0f)
        {
            go.transform.SetPositionAndRotation(pos, rot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
            go.transform.SetPositionAndRotation(
                Vector3.Lerp(go.transform.position, pos, k),
                Quaternion.Slerp(go.transform.rotation, rot, k));
        }
    }

    Quaternion TargetRotation(Vector3 pos, Transform firstImage)
    {
        Quaternion rot;
        if (faceCamera && arCamera != null)
        {
            // sisi baca teks adalah -Z, jadi +Z diarahkan menjauhi kamera
            Vector3 away = Vector3.ProjectOnPlane(pos - arCamera.transform.position, Vector3.up);
            if (away.sqrMagnitude < 1e-6f) away = arCamera.transform.forward;
            rot = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        else
        {
            rot = Quaternion.LookRotation(firstImage.forward, firstImage.up);
        }
        return flipFacing ? rot * Quaternion.Euler(0f, 180f, 0f) : rot;
    }
}
