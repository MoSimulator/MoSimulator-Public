using UnityEngine;
using System.Collections.Generic;
using Games.Rebuilt.GameManagement;
using Games.Rebuilt.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Games.Rebuilt.LEDs
{
    [System.Serializable]
    public class LEDStrip
    {
        public string stripName = "Strip";
        public int ledCount = 101;
        public float ledSpacing = 0.1f;
        public Vector3 localPosition = Vector3.zero;
        public Vector3 localRotation = Vector3.zero;
        public Vector3 localScale = Vector3.one;
        public Color defaultColor = Color.white;

        [HideInInspector] public GameObject[] leds;
        [HideInInspector] public MaterialPropertyBlock[] propBlocks;
        [HideInInspector] public Renderer[] renderers;
        [HideInInspector] public Transform container;
        [HideInInspector] public Color lastColor = Color.clear;
    }

    [ExecuteInEditMode]
    public class LEDStripController : MonoBehaviour
    {
        private enum LEDState
        {
        }

        [Header("Setup")] public GameObject ledPrefab;

        [Header("Emission")] public float emissionIntensity = 5f;

        [Header("Strips")] public List<LEDStrip> strips = new List<LEDStrip>();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private bool _ready = false;
        private LEDState _lastState;

        [Header("Runtime Debug")] [SerializeField]
        private LEDState currentLEDState;

        [SerializeField] private float currentTimer;
        [SerializeField] private GameState currentGameState;
        [SerializeField] private RobotState currentRobotState;
        [SerializeField] private bool hasRobotbase;
        [SerializeField] private Color currentColor = Color.clear;

        protected RebuiltRobotBase Robotbase { get; private set; }

#if UNITY_EDITOR
        void OnEnable()
        {
            if (ledPrefab == null) return;
            EditorApplication.delayCall += TryBuild;
        }

        void OnValidate()
        {
            if (ledPrefab == null) return;
            EditorApplication.delayCall += TryBuild;
        }

        void TryBuild()
        {
            if (this == null || gameObject == null) return;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool inPrefabMode = stage != null && stage.IsPartOfPrefabContents(gameObject);
            bool inLoadedScene = gameObject.scene.isLoaded && !gameObject.scene.path.Equals("");

            if (!inPrefabMode && !inLoadedScene) return;

            BuildAllStrips();
        }
#endif

        void OnDisable()
        {
            ClearAllLEDs();
        }

        // Clears only the LED instances inside each container, leaving containers intact
        void ClearAllLEDs()
        {
            if (strips == null) return;
            foreach (var strip in strips)
                ClearStripLEDs(strip);
        }

        void ClearStripLEDs(LEDStrip strip)
        {
            if (strip.leds != null)
            {
                foreach (var led in strip.leds)
                {
                    if (led != null)
                        DestroyImmediate(led, true);
                }
            }

            strip.leds = null;
            strip.propBlocks = null;
            strip.renderers = null;
            strip.lastColor = Color.clear;
            // container is intentionally kept alive
        }

        void BuildAllStrips()
        {
            if (strips == null || ledPrefab == null) return;

            for (int s = 0; s < strips.Count; s++)
                BuildStrip(strips[s], s);
        }

        void BuildStrip(LEDStrip strip, int index)
        {
            // Clear existing LEDs but keep the container
            ClearStripLEDs(strip);

            // Reuse existing container or create a new one
            string containerName = $"LEDStrip_{index}_{strip.stripName}";

            if (strip.container == null)
            {
                Transform existing = transform.Find(containerName);
                if (existing != null)
                {
                    strip.container = existing;
                }
                else
                {
                    GameObject containerObject = new GameObject(containerName);
                    containerObject.transform.SetParent(transform, false);
                    strip.container = containerObject.transform;
                }
            }

            strip.container.localPosition = strip.localPosition;
            strip.container.localRotation = Quaternion.Euler(strip.localRotation);
            strip.container.localScale = strip.localScale;

            // Populate LEDs
            strip.leds = new GameObject[strip.ledCount];
            strip.propBlocks = new MaterialPropertyBlock[strip.ledCount];
            strip.renderers = new Renderer[strip.ledCount];

            for (int i = 0; i < strip.ledCount; i++)
            {
                strip.leds[i] = Instantiate(ledPrefab, strip.container);
                strip.leds[i].hideFlags = HideFlags.DontSave;
                strip.leds[i].transform.localPosition = new Vector3(i * strip.ledSpacing, 0, 0);
                strip.leds[i].name = $"LED_{i}";

                strip.renderers[i] = strip.leds[i].GetComponentInChildren<Renderer>();
                strip.propBlocks[i] = new MaterialPropertyBlock();
            }

            strip.lastColor = Color.clear;
            SetAll(strip, strip.defaultColor);
        }

        void Awake()
        {
            if (Application.isPlaying)
            {
                Robotbase = GetComponent<RebuiltRobotBase>();
                BuildAllStrips();
            }
        }

        void Update()
        {
            if (!Application.isPlaying) return;

            if (Robotbase == null)
                Robotbase = GetComponent<RebuiltRobotBase>();

            hasRobotbase = Robotbase != null;

            if (!_ready)
            {
                if (BaseGameManager.Instance == null || RebuiltGameManager.Instance == null)
                    return;

                _ready = true;
            }

            currentTimer = BaseGameManager.Instance.Timer;
            currentGameState = RebuiltGameManager.Instance.GameState;
            currentRobotState = RebuiltGameManager.Instance.RobotState;

            if (Robotbase == null)
                return;
        }

        void ApplyState(LEDState state)
        {
            currentLEDState = state;

            switch (state)
            {
            }

            _lastState = state;
        }

        // -- Per-strip API --

        public void SetLED(LEDStrip strip, int index, Color color)
        {
            SetLED(strip, index, color, emissionIntensity);
        }

        public void SetLED(LEDStrip strip, int index, Color color, float intensity)
        {
            if (strip.renderers == null || strip.propBlocks == null) return;
            if (index < 0 || index >= strip.renderers.Length) return;
            if (strip.renderers[index] == null || strip.propBlocks[index] == null) return;

            strip.propBlocks[index].SetColor(BaseColorId, color);
            strip.propBlocks[index].SetColor(ColorId, color);
            strip.propBlocks[index].SetColor(EmissionColorId, color * intensity);

            strip.renderers[index].SetPropertyBlock(strip.propBlocks[index]);
        }

        public void SetAll(LEDStrip strip, Color color)
        {
            if (color == strip.lastColor) return;

            strip.lastColor = color;

            for (int i = 0; i < strip.ledCount; i++)
                SetLED(strip, i, color, emissionIntensity);
        }

        // -- Index-based convenience overloads --

        public void SetLED(int stripIndex, int ledIndex, Color color)
        {
            if (stripIndex < 0 || stripIndex >= strips.Count) return;
            SetLED(strips[stripIndex], ledIndex, color);
        }

        public void SetLED(int stripIndex, int ledIndex, Color color, float intensity)
        {
            if (stripIndex < 0 || stripIndex >= strips.Count) return;
            SetLED(strips[stripIndex], ledIndex, color, intensity);
        }

        public void SetAll(int stripIndex, Color color)
        {
            if (stripIndex < 0 || stripIndex >= strips.Count) return;
            SetAll(strips[stripIndex], color);
        }

        public void SetAllStrips(Color color)
        {
            foreach (var strip in strips)
                SetAll(strip, color);
        }
    }
}