using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Swaps the materials on the selected MeshRenderers for the perceptually closest
    /// material found in the project's Materials library.
    /// </summary>
    public class ReplaceColors : EditorWindow
    {
        const string LibraryRoot = "Assets/Materials";
        const string ExclusionsPrefKey = "MoSim.ReplaceColors.Exclusions";
        const float MaxLightnessOffset = 0.5f;

        /// <summary>Library materials that should never be picked as a match.</summary>
        static readonly string[] DefaultExclusions =
        {
            "DiamondPlate",
            "BargePanels",
            "DarkSteel",
            "ReefPurple",
            "BumperWhite",
            "AlgaeLogo",
            "Carpet",
            "Steel",
            "SteelBothSides",
            "New Material",
            "Fuel 1"
        };

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        class LibraryEntry
        {
            public Material Material;
            public Color Color;
            public Vector3 Lab;
            public bool Transparent;
        }

        struct Replacement
        {
            public Renderer Renderer;
            public int Slot;
            public Material From;
            public Material To;
            public Color Adjusted;
            public float Distance;
        }

        readonly List<LibraryEntry> _library = new List<LibraryEntry>();
        readonly List<Replacement> _replacements = new List<Replacement>();
        readonly List<string> _exclusions = new List<string>();

        bool _includeChildren = true;
        bool _skipTextured = true;
        bool _skipLibraryMaterials = true;
        bool _showExclusions = true;
        float _lightnessOffset;
        Vector2 _scroll;

        [MenuItem("Tools/Replace Robot Colors", priority = 10)]
        static void Open()
        {
            GetWindow<ReplaceColors>("Replace Robot Colors").Show();
        }

        void OnEnable()
        {
            LoadExclusions();
            RefreshLibrary();
            Selection.selectionChanged += BuildPreview;
            BuildPreview();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= BuildPreview;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField($"Library: {_library.Count} materials in {LibraryRoot}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _skipTextured = EditorGUILayout.Toggle(
                new GUIContent("Ignore Textured Materials", "Exclude library materials that use an albedo texture, since their flat color is not representative."),
                _skipTextured);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshLibrary();
                BuildPreview();
            }

            EditorGUI.BeginChangeCheck();
            _includeChildren = EditorGUILayout.Toggle("Include Children", _includeChildren);
            _skipLibraryMaterials = EditorGUILayout.Toggle(
                new GUIContent("Skip Library Materials", "Leave slots alone when they already reference a material from the library."),
                _skipLibraryMaterials);
            _lightnessOffset = EditorGUILayout.Slider(
                new GUIContent("Lightness Offset",
                    "Correction applied to the source color before matching, as a shift along HSL lightness. " +
                    "Negative if the incoming materials are on average lighter than they should be, positive if darker. " +
                    "Hue and saturation are preserved, so a pale purple shifted down looks for a deep purple."),
                _lightnessOffset, -MaxLightnessOffset, MaxLightnessOffset);
            EditorGUILayout.LabelField(" ", LightnessOffsetHint(), EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck()) BuildPreview();

            if (GUILayout.Button("Refresh")) { RefreshLibrary(); BuildPreview(); }

            DrawExclusions();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Pending replacements: {_replacements.Count}", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (Replacement r in _replacements)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{r.Renderer.name} [{r.Slot}]", GUILayout.Width(180));
                DrawSwatch(GetColor(r.From));
                if (!Mathf.Approximately(_lightnessOffset, 0f)) DrawSwatch(r.Adjusted);
                EditorGUILayout.LabelField(r.From != null ? r.From.name : "(none)", GUILayout.Width(140));
                EditorGUILayout.LabelField("→", GUILayout.Width(16));
                DrawSwatch(GetColor(r.To));
                EditorGUILayout.LabelField(r.To.name, GUILayout.Width(140));
                EditorGUILayout.LabelField($"Δ{r.Distance:F1}", GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(_replacements.Count == 0))
            {
                if (GUILayout.Button("Replace", GUILayout.Height(28))) Apply();
            }
        }

        void DrawExclusions()
        {
            _showExclusions = EditorGUILayout.Foldout(_showExclusions, $"Excluded Materials ({_exclusions.Count})", true);
            if (!_showExclusions) return;

            EditorGUI.indentLevel++;
            int removeAt = -1;

            for (int i = 0; i < _exclusions.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField(_exclusions[i]);
                if (EditorGUI.EndChangeCheck())
                {
                    _exclusions[i] = name;
                    CommitExclusions();
                }

                if (GUILayout.Button("−", GUILayout.Width(22))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0)
            {
                _exclusions.RemoveAt(removeAt);
                CommitExclusions();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add"))
            {
                _exclusions.Add(string.Empty);
                _showExclusions = true;
            }
            if (GUILayout.Button("Reset to Defaults"))
            {
                _exclusions.Clear();
                _exclusions.AddRange(DefaultExclusions);
                CommitExclusions();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        bool IsExcluded(string materialName)
        {
            return _exclusions.Any(e => string.Equals(e.Trim(), materialName, System.StringComparison.OrdinalIgnoreCase));
        }

        void CommitExclusions()
        {
            SaveExclusions();
            RefreshLibrary();
            BuildPreview();
        }

        void LoadExclusions()
        {
            _exclusions.Clear();
            string stored = EditorPrefs.GetString(ExclusionsPrefKey, null);
            if (stored == null)
            {
                _exclusions.AddRange(DefaultExclusions);
                return;
            }

            _exclusions.AddRange(stored.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0));
        }

        void SaveExclusions()
        {
            EditorPrefs.SetString(ExclusionsPrefKey, string.Join("\n", _exclusions.Select(s => s.Trim()).Where(s => s.Length > 0)));
        }

        static void DrawSwatch(Color c)
        {
            Rect rect = GUILayoutUtility.GetRect(18, 16, GUILayout.Width(18));
            EditorGUI.DrawRect(rect, new Color(c.r, c.g, c.b, 1f));
        }

        void RefreshLibrary()
        {
            _library.Clear();
            var excluded = new HashSet<string>(
                _exclusions.Select(s => s.Trim()).Where(s => s.Length > 0),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { LibraryRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || !HasColorProperty(mat, out int id)) continue;
                if (excluded.Contains(mat.name)) continue;
                if (_skipTextured && HasAlbedoTexture(mat)) continue;

                Color c = GetColor(mat);
                _library.Add(new LibraryEntry
                {
                    Material = mat,
                    Color = c,
                    Lab = RgbToLab(c),
                    Transparent = IsTransparent(mat)
                });
            }
        }

        void BuildPreview()
        {
            _replacements.Clear();

            foreach (Renderer renderer in CollectRenderers())
            {
                Material[] mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material current = mats[i];
                    if (current != null && IsExcluded(current.name)) continue;
                    if (_skipLibraryMaterials && current != null && _library.Any(e => e.Material == current)) continue;

                    Color source = AdjustedColor(GetColor(current));
                    LibraryEntry best = FindClosest(RgbToLab(source), IsTransparent(current), out float distance);
                    if (best == null || best.Material == current) continue;

                    _replacements.Add(new Replacement
                    {
                        Renderer = renderer,
                        Slot = i,
                        From = current,
                        To = best.Material,
                        Adjusted = source,
                        Distance = distance
                    });
                }
            }

            Repaint();
        }

        IEnumerable<Renderer> CollectRenderers()
        {
            var seen = new HashSet<Renderer>();
            foreach (GameObject go in Selection.gameObjects)
            {
                IEnumerable<MeshRenderer> found = _includeChildren
                    ? go.GetComponentsInChildren<MeshRenderer>(true)
                    : go.GetComponents<MeshRenderer>();

                foreach (MeshRenderer r in found)
                    if (seen.Add(r)) yield return r;
            }
        }

        /// <summary>
        /// Source color with the lightness correction applied along the HSL lightness axis, which
        /// keeps hue and saturation intact: darkening a pale purple yields a deep purple rather
        /// than a dark gray, because HSL saturation is preserved as lightness drops.
        /// </summary>
        Color AdjustedColor(Color color)
        {
            if (Mathf.Approximately(_lightnessOffset, 0f)) return color;

            RgbToHsl(color, out float h, out float s, out float l);
            return HslToRgb(h, s, Mathf.Clamp01(l + _lightnessOffset), color.a);
        }

        string LightnessOffsetHint()
        {
            if (Mathf.Approximately(_lightnessOffset, 0f)) return "No correction — match source colors as authored.";
            return _lightnessOffset < 0f
                ? "Sources treated as too light; matched against darker shades of the same hue."
                : "Sources treated as too dark; matched against lighter shades of the same hue.";
        }

        LibraryEntry FindClosest(Vector3 lab, bool transparent, out float distance)
        {
            distance = float.MaxValue;
            LibraryEntry best = null;

            foreach (LibraryEntry entry in _library)
            {
                if (entry.Transparent != transparent) continue;

                float d = DeltaE94(lab, entry.Lab);
                if (d >= distance) continue;
                distance = d;
                best = entry;
            }

            return best;
        }

        /// <summary>
        /// CIE94 color difference with lightness de-emphasized (kL = 2). Plain Euclidean Lab
        /// distance lets a lightness gap outrank hue, so a purple could land on a gray or a blue
        /// that happened to sit closer in raw a*/b*; weighting chroma and hue above lightness keeps
        /// the match inside the right color family.
        /// </summary>
        static float DeltaE94(Vector3 reference, Vector3 sample)
        {
            const float kL = 2f;

            float c1 = Mathf.Sqrt(reference.y * reference.y + reference.z * reference.z);
            float c2 = Mathf.Sqrt(sample.y * sample.y + sample.z * sample.z);

            float dL = reference.x - sample.x;
            float dC = c1 - c2;
            float da = reference.y - sample.y;
            float db = reference.z - sample.z;
            float dH2 = Mathf.Max(0f, da * da + db * db - dC * dC);

            float sC = 1f + 0.045f * c1;
            float sH = 1f + 0.015f * c1;

            float termL = dL / kL;
            float termC = dC / sC;

            return Mathf.Sqrt(termL * termL + termC * termC + dH2 / (sH * sH));
        }

        void Apply()
        {
            var byRenderer = _replacements.GroupBy(r => r.Renderer);
            int count = 0;

            foreach (var group in byRenderer)
            {
                Renderer renderer = group.Key;
                Undo.RecordObject(renderer, "Replace Colors");

                Material[] mats = renderer.sharedMaterials;
                foreach (Replacement r in group)
                {
                    mats[r.Slot] = r.To;
                    count++;
                }
                renderer.sharedMaterials = mats;

                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }

            Debug.Log($"Replace Colors: assigned {count} material slot(s) across {byRenderer.Count()} renderer(s).");
            BuildPreview();
        }

        static bool HasColorProperty(Material mat, out int id)
        {
            if (mat != null && mat.HasProperty(BaseColorId)) { id = BaseColorId; return true; }
            if (mat != null && mat.HasProperty(ColorId)) { id = ColorId; return true; }
            id = 0;
            return false;
        }

        /// <summary>
        /// True when a material actually renders as transparent, so an opaque source never matches a
        /// see-through library material of the same hue. Only the surface mode and render queue count:
        /// albedo alpha is ignored by an opaque shader, and FBX-imported materials routinely carry a
        /// meaningless alpha (the "R_G_B_A" named ones often ship alpha 0 while rendering solid).
        /// </summary>
        static bool IsTransparent(Material mat)
        {
            if (mat == null) return false;
            if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f) return true;
            if (mat.HasProperty("_Mode") && mat.GetFloat("_Mode") >= 2f) return true;
            return mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static bool HasAlbedoTexture(Material mat)
        {
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) return true;
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) return true;
            return false;
        }

        /// <summary>
        /// Albedo color of a material in sRGB. Material values are stored linear in a linear-space
        /// project, so they are gamma encoded here for both the Lab math and the preview swatches.
        /// </summary>
        static Color GetColor(Material mat)
        {
            if (mat == null || !HasColorProperty(mat, out int id)) return Color.white;
            Color c = mat.GetColor(id);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
        }

        static void RgbToHsl(Color c, out float h, out float s, out float l)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float delta = max - min;

            l = (max + min) * 0.5f;

            if (Mathf.Approximately(delta, 0f))
            {
                h = 0f;
                s = 0f;
                return;
            }

            s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

            if (max == c.r) h = (c.g - c.b) / delta + (c.g < c.b ? 6f : 0f);
            else if (max == c.g) h = (c.b - c.r) / delta + 2f;
            else h = (c.r - c.g) / delta + 4f;

            h /= 6f;
        }

        static Color HslToRgb(float h, float s, float l, float a)
        {
            if (Mathf.Approximately(s, 0f)) return new Color(l, l, l, a);

            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;

            return new Color(HueToChannel(p, q, h + 1f / 3f), HueToChannel(p, q, h), HueToChannel(p, q, h - 1f / 3f), a);
        }

        static float HueToChannel(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        /// <summary>sRGB to CIE L*a*b* (D65) so "closest" matches perceived difference, not raw RGB distance.</summary>
        static Vector3 RgbToLab(Color c)
        {
            float r = SrgbToLinear(c.r);
            float g = SrgbToLinear(c.g);
            float b = SrgbToLinear(c.b);

            float x = (r * 0.4124564f + g * 0.3575761f + b * 0.1804375f) / 0.95047f;
            float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
            float z = (r * 0.0193339f + g * 0.1191920f + b * 0.9503041f) / 1.08883f;

            x = LabF(x);
            y = LabF(y);
            z = LabF(z);

            return new Vector3(116f * y - 16f, 500f * (x - y), 200f * (y - z));
        }

        static float SrgbToLinear(float v)
        {
            return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        static float LabF(float t)
        {
            const float e = 216f / 24389f;
            const float k = 24389f / 27f;
            return t > e ? Mathf.Pow(t, 1f / 3f) : (k * t + 16f) / 116f;
        }
    }
}
