using MoSimCore.Enums;
using UnityEngine;
using Games.Rebuilt.Scoring;

namespace Games.Rebuilt.FieldScripts
{
    public class HubLights : MonoBehaviour
    {
        [SerializeField] private Alliance alliance;

        [SerializeField] private Color32 redCol = new Color(255.0f, 0.0f, 0.0f, 255.0f);
        [SerializeField] private Color32 blueCol = new Color(0.0f, 0.0f, 255.0f, 255.0f);
        [SerializeField] private Color32 purpleCol = new Color(218.0f, 177.0f, 218.0f, 255.0f);
        [SerializeField] private Color32 greenCol = new Color(0.0f, 255.0f, 0.0f, 255.0f);
        private Color rainbowCol;

        private Color _allianceColor;

        [SerializeField] private GameObject lightsHolder;

        [SerializeField] private Renderer panel1Renderer;
        [SerializeField] private Renderer panel2Renderer;
        [SerializeField] private Renderer panel3Renderer;
        [SerializeField] private Renderer panel4Renderer;

        [SerializeField] private bool flipPanel1 = false;
        [SerializeField] private bool flipPanel2 = false;
        [SerializeField] private bool flipPanel3 = false;
        [SerializeField] private bool flipPanel4 = false;

        private bool[] _panelFlips;

        private Renderer[] _panelRenderers;

        private MaterialPropertyBlock _mpb;

        private static readonly int SweepProgressID = Shader.PropertyToID("_SweepProgress");
        private static readonly int AllianceColorID = Shader.PropertyToID("_AllianceColor");
        private static readonly int SweepColorID = Shader.PropertyToID("_SweepColor");
        private static readonly int BandWidthID = Shader.PropertyToID("_BandWidth");

        [SerializeField] private float bandWidth = 0.3f;

        [SerializeField, Tooltip("Close, Far, Left, Right")] private float[] phaseOffsets = { -0.1f, -0.1f, 0.4f, 0.4f };

        private float _pulseTime;
        private float _chaseOffset;

        private float _sweepDuration = 1.2f;
        private float _holdDuration = 0.2f;
        private float _resetDuration = 0.1f;

        public enum Shift
        {
            ACTIVE,
            WARNING_PULSE,
            TRANSITION_WON,
            INACTIVE,
            WHITE,
            PURPLE,
            GREEN
        }

        public float cycleSpeed = 0.1f;

        // This tracks the current position on the color wheel (0.0 to 1.0)
        private float currentHue = 0f;


        [SerializeField] public Shift shift;

        public void SetShift(Shift newShift)
        {
            if (newShift == shift) return;
            shift = newShift;
            _pulseTime = 0;
            _chaseOffset = 0;
        }

        private void SetPanelSweep(Renderer r, float sweepProgress, bool flip)
        {
            _mpb.SetColor(AllianceColorID, _allianceColor);
            _mpb.SetColor(SweepColorID, Color.white);
            _mpb.SetFloat(SweepProgressID, flip ? 1f - sweepProgress : sweepProgress);
            r.SetPropertyBlock(_mpb);
        }

        private void SetPanelFlat(Renderer r, Color c)
        {
            _mpb.SetColor(AllianceColorID, c);
            _mpb.SetColor(SweepColorID, c);
            _mpb.SetFloat(SweepProgressID, 1f);
            r.SetPropertyBlock(_mpb);
        }

        private void SetPanelChase(Renderer r, float globalProgress, float phaseOffset, bool flip)
        {
            float local = (globalProgress + phaseOffset) % 1f;

            float bandCenter = flip ? 1f - local : local;

            float margin = bandWidth * 0.5f + 0.05f;
            bandCenter = Mathf.Lerp(-margin, 1f + margin, flip ? 1f - local : local);

            _mpb.SetColor(AllianceColorID, _allianceColor);
            _mpb.SetColor(SweepColorID, Color.white);
            _mpb.SetFloat(SweepProgressID, bandCenter);
            _mpb.SetFloat(BandWidthID, bandWidth);
            r.SetPropertyBlock(_mpb);
        }

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            _allianceColor = alliance == Alliance.Blue
                ? (Color)blueCol
                : (Color)redCol;

            _panelRenderers = new[] { panel1Renderer, panel2Renderer, panel3Renderer, panel4Renderer };
            _panelFlips = new[] { flipPanel1, flipPanel2, flipPanel3, flipPanel4 };

            foreach (var r in _panelRenderers)
            {
                _mpb.SetColor(AllianceColorID, _allianceColor);
                _mpb.SetColor(SweepColorID, Color.Lerp(Color.white, Color.gray, 0.4f));
                _mpb.SetFloat(SweepProgressID, 1f);
                r.SetPropertyBlock(_mpb);
            }
        }

        private void Update()
        {
            _pulseTime += Time.deltaTime;

            currentHue += cycleSpeed * Time.deltaTime;

            // 2. Keep the value between 0.0 and 1.0 so it loops indefinitely
            if (currentHue > 1.0f)
            {
                currentHue -= 1.0f;
            }
            rainbowCol = Color.HSVToRGB(currentHue, 1f, 1f);

            if (HubScoring.godMode && shift != Shift.INACTIVE)
            {
                _chaseOffset += Time.deltaTime / _sweepDuration;
                for (int p = 0; p < _panelRenderers.Length; p++)
                {
                    foreach (var r in _panelRenderers) SetPanelFlat(r, rainbowCol);
                }
                return;
            }
            switch (shift)
            {
                case Shift.ACTIVE:
                    foreach (var r in _panelRenderers) SetPanelFlat(r, _allianceColor);
                    break;
                case Shift.WARNING_PULSE:
                    float pulse = (Mathf.Sin(_pulseTime * Mathf.PI * 4f) + 1f) / 2f;
                    Color pulseColor = Color.Lerp(Color.Lerp(Color.white, Color.gray, 0.1f), _allianceColor, pulse);
                    foreach (var r in _panelRenderers) SetPanelFlat(r, pulseColor);
                    break;
                case Shift.TRANSITION_WON:
                    _chaseOffset += Time.deltaTime / _sweepDuration;

                    // float[] phaseOffsets = { 0f, 0.25f, 0.5f, 0.75f };

                    for (int p = 0; p < _panelRenderers.Length; p++)
                    {
                        SetPanelChase(_panelRenderers[p], _chaseOffset, phaseOffsets[p], _panelFlips[p]);
                    }
                    break;
                case Shift.INACTIVE:
                    foreach (var r in _panelRenderers) SetPanelFlat(r, Color.Lerp(Color.white, Color.gray, 0.3f));
                    break;
                case Shift.WHITE:
                    foreach (var r in _panelRenderers) SetPanelFlat(r, Color.Lerp(Color.white, Color.gray, 0.1f));
                    break;
                case Shift.PURPLE:
                    foreach (var r in _panelRenderers) SetPanelFlat(r, (Color)purpleCol);
                    break;
                case Shift.GREEN:
                    foreach (var r in _panelRenderers) SetPanelFlat(r, (Color)greenCol);
                    break;
            }
        }
    }
}
