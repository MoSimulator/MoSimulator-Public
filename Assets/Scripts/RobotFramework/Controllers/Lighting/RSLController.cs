using System.Collections;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using UnityEngine;

namespace RobotFramework.Controllers.Lighting
{
    public class RSLController : MonoBehaviour
    {
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

        private const float FlashDelay = 0.4f;
        [SerializeField] private Color _onColor = new(191f / 255f, 59f / 255f, 0f, 1f);
        [SerializeField] private Color _offColor = new(191f / 255f, 59f / 255f, 0f, 1f);
        [SerializeField] private bool _useEmission = true;
        [SerializeField, ColorUsage(false, true)] private Color _onEmissionColor = new Color(191f / 255f, 59f / 255f, 0f, 1f) * 3f;
        [SerializeField, ColorUsage(false, true)] private Color _offEmissionColor = new(128f / 255f, 39f / 255f, 0f, 1f);

        private RSLSettings _rslSettings;
        private RobotBase _robotBase;
        private MaterialPropertyBlock _propertyBlock;
        private int _baseColorPropertyId;
        private bool _hasEmissionProperty;

        private void Start()
        {
            _robotBase = GetComponent<RobotBase>();
            if (_robotBase == null)
            {
                Debug.LogWarning("RSLController requires a RobotBase component.", this);
                enabled = false;
                return;
            }

            _rslSettings = _robotBase.RSLSettings;
            if (!TryInitializeRenderer())
            {
                enabled = false;
                return;
            }

            StartCoroutine(RSLFlash());
        }

        private bool TryInitializeRenderer()
        {
            var renderer = _rslSettings.RSLMesh;
            if (renderer == null)
            {
                Debug.LogWarning("RSL Mesh is not set.", _robotBase.gameObject);
                return false;
            }

            var materials = renderer.sharedMaterials;
            var materialIndex = _rslSettings.materialIndex;
            if (materialIndex < 0 || materialIndex >= materials.Length || materials[materialIndex] == null)
            {
                Debug.LogWarning("RSL material slot is invalid or has no material assigned.", _robotBase.gameObject);
                return false;
            }

            var material = materials[materialIndex];
            if (material.HasProperty(BaseColorPropertyId))
            {
                _baseColorPropertyId = BaseColorPropertyId;
            }
            else if (material.HasProperty(LegacyColorPropertyId))
            {
                _baseColorPropertyId = LegacyColorPropertyId;
            }
            else
            {
                Debug.LogWarning("RSL material must expose a _BaseColor or _Color shader property.", _robotBase.gameObject);
                return false;
            }

            _hasEmissionProperty = material.HasProperty(EmissionColorPropertyId);
            if (_useEmission && !_hasEmissionProperty)
            {
                Debug.LogWarning("RSL material has no _EmissionColor property; flashing will use base color only.", _robotBase.gameObject);
            }

            _propertyBlock = new MaterialPropertyBlock();
            return true;
        }

        private IEnumerator RSLFlash()
        {
            var wait = new WaitForSeconds(Mathf.Max(0f, FlashDelay));

            while (true)
            {
                while (BaseGameManager.Instance.RobotState == RobotState.Enabled)
                {
                    SetLightState(true);
                    yield return wait;
                    SetLightState(false);
                    yield return wait;
                }

                SetLightState(true);
                yield return null;
            }
        }

        private void SetLightState(bool isOn)
        {
            var renderer = _rslSettings.RSLMesh;
            var materialIndex = _rslSettings.materialIndex;

            renderer.GetPropertyBlock(_propertyBlock, materialIndex);
            _propertyBlock.SetColor(_baseColorPropertyId, isOn ? _onColor : _offColor);
            if (_useEmission && _hasEmissionProperty)
            {
                _propertyBlock.SetColor(EmissionColorPropertyId, isOn ? _onEmissionColor : _offEmissionColor);
            }

            renderer.SetPropertyBlock(_propertyBlock, materialIndex);
        }
    }
}
