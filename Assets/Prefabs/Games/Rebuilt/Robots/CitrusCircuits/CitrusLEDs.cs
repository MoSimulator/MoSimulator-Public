using System.Collections.Generic;
using Games.Rebuilt.GameManagement;
using Games.Rebuilt.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

#if UNITY_EDITOR
#endif

namespace Prefabs.Games.Rebuilt.Robots.CitrusCircuits
{
    [System.Serializable]
    public class LEDStrip
    {
        public string stripName = "Strip";
        public int ledCount = 101;
        public float ledSpacing = 0.1f;

        public Vector3 localPosition;
        public Vector3 localRotation;
        public Vector3 localScale = Vector3.one;

        public Color defaultColor = Color.white;

        [HideInInspector] public Transform container;
        [HideInInspector] public GameObject[] leds;
        [HideInInspector] public Renderer[] renderers;
        [HideInInspector] public MaterialPropertyBlock[] props;

        [HideInInspector] public Color lastColor;
    }

    public class CitrusLEDs : MonoBehaviour
    {
        // STATE SYSTEM
        private enum LEDState
        {
            Disabled,
            Idle,
            Active,
            Error,
            Intaking,
            Outtaking
        }

        private static readonly Color DisabledColor = Color.cyan;
        private static readonly Color IdleColor = Color.white;
        private static readonly Color ActiveColor = Color.green;
        private static readonly Color ErrorColor = Color.red;

        [Header("Setup")]
        public GameObject ledPrefab;

        [Header("Emission")]
        public float emissionIntensity = 5f;

        [Header("Strips")]
        public List<LEDStrip> strips = new();

        [Header("Runtime Debug")]
        [SerializeField] private LEDState currentState;
        private LEDState _lastState = (LEDState)(-1);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        RebuiltRobotBase Robotbase;
        CitrusCircuits citrus;
        bool _ready;

        float currentTimer;
        GameState currentGameState;
        RobotState currentRobotState;

        // FLASH INT
        float _flashTimer;
        bool _flashOn;
        Color _lastIntakeColor = Color.clear;

        // FLASH OUT
        float _outFlashTimer;
        bool _outFlashOn;
        Color _lastOuttakeColor = Color.clear;

        void Awake()
        {
            if (!Application.isPlaying) return;

            Robotbase = GetComponent<RebuiltRobotBase>();
            citrus = GetComponent<CitrusCircuits>();
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            BuildAll();
        }

        void Update()
        {
            if (!Application.isPlaying) return;

            if (Robotbase == null)
                Robotbase = GetComponent<RebuiltRobotBase>();

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

            // STATE SELECTION
            LEDState target = LEDState.Idle;

            if (currentRobotState == RobotState.Disabled)
                target = LEDState.Disabled;
            else if (Robotbase.OuttakeAction || Robotbase.AutoPass)
                target = LEDState.Outtaking;
            else if (Robotbase.IntakeAction)
                target = LEDState.Intaking;
            else
                target = LEDState.Active;

            // INTAKE FLASH
            if (currentState == LEDState.Intaking)
            {
                _flashTimer += Time.deltaTime;

                if (_flashTimer >= 0.08f)
                {
                    _flashTimer = 0f;
                    _flashOn = !_flashOn;

                    Color c = _flashOn
                        ? Color.yellow
                        : new Color(1f, 0.25f, 0f);

                    if (c != _lastIntakeColor)
                    {
                        _lastIntakeColor = c;

                        foreach (var strip in strips)
                            SetAllFast(strip, c);
                    }
                }
            }
            else
            {
                _flashTimer = 0f;
                _flashOn = false;
            }
            
            // OUTTAKE FLASH
            if (currentState == LEDState.Outtaking)
            {
                _outFlashTimer += Time.deltaTime;

                if (_outFlashTimer >= 0.09f)
                {
                    _outFlashTimer = 0f;
                    _outFlashOn = !_outFlashOn;

                    Color c;

                    if (citrus != null && citrus.ShooterAligned())
                        c = _outFlashOn ? Color.yellow : Color.blue;
                    else
                        c = _outFlashOn ? Color.red : Color.black;

                    if (c != _lastOuttakeColor)
                    {
                        _lastOuttakeColor = c;

                        foreach (var strip in strips)
                            SetAllFast(strip, c);
                    }
                }
            }
            else
            {
                _outFlashTimer = 0f;
                _outFlashOn = false;
            }
            
            // APPLY STATE
            if (target != _lastState)
            {
                ApplyState(target);
                _lastState = target;
            }
        }
        
        // STATE APPLY
        void ApplyState(LEDState state)
        {
            currentState = state;

            Color c = state switch
            {
                LEDState.Disabled => DisabledColor,
                LEDState.Idle => IdleColor,
                LEDState.Active => ActiveColor,
                LEDState.Error => ErrorColor,
                _ => Color.white
            };

            foreach (var strip in strips)
                SetAllFast(strip, c);
        }
        
        // FAST STRIP WRITER
        void SetAllFast(LEDStrip strip, Color color)
        {
            for (int i = 0; i < strip.ledCount; i++)
            {
                var r = strip.renderers[i];
                if (r == null) continue;

                var mpb = strip.props[i];

                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mpb.SetColor(EmissionId, color * emissionIntensity);

                r.SetPropertyBlock(mpb);
            }
        }
        
        // BUILD
        void BuildAll()
        {
            if (!Application.isPlaying) return;
            if (ledPrefab == null) return;

            ClearAll();

            for (int i = 0; i < strips.Count; i++)
                BuildStrip(strips[i], i);
        }

        void BuildStrip(LEDStrip strip, int index)
        {
            var go = new GameObject($"LEDStrip_{index}_{strip.stripName}");
            go.transform.SetParent(transform, false);

            strip.container = go.transform;

            strip.container.localPosition = strip.localPosition;
            strip.container.localRotation = Quaternion.Euler(strip.localRotation);
            strip.container.localScale = strip.localScale;

            strip.leds = new GameObject[strip.ledCount];
            strip.renderers = new Renderer[strip.ledCount];
            strip.props = new MaterialPropertyBlock[strip.ledCount];

            for (int i = 0; i < strip.ledCount; i++)
            {
                var led = Instantiate(ledPrefab, strip.container);
                led.name = $"LED_{i}";
                led.transform.localPosition = new Vector3(i * strip.ledSpacing, 0, 0);

                strip.leds[i] = led;
                strip.renderers[i] = led.GetComponentInChildren<Renderer>();
                strip.props[i] = new MaterialPropertyBlock();
            }

            SetAllFast(strip, strip.defaultColor);
        }
        
        // CLEANUP
        void ClearAll()
        {
            if (!Application.isPlaying) return;

            foreach (var strip in strips)
            {
                if (strip.container != null)
                    Destroy(strip.container.gameObject);

                strip.container = null;
                strip.leds = null;
                strip.renderers = null;
                strip.props = null;
            }
        }
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (strips == null)
                return;

            foreach (var strip in strips)
            {
                if (strip == null)
                    continue;

                Matrix4x4 stripMatrix =
                    transform.localToWorldMatrix *
                    Matrix4x4.TRS(
                        strip.localPosition,
                        Quaternion.Euler(strip.localRotation),
                        strip.localScale
                    );

                Gizmos.matrix = stripMatrix;
                
                for (int i = 0; i < strip.ledCount; i++)
                {
                    Vector3 pos = new Vector3(i * strip.ledSpacing, 0f, 0f);

                    Vector3 size = new Vector3(
                        0.005f,
                        0.002f,
                        0.005f
                    );

                    Gizmos.DrawCube(pos, size);
                }
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }

}