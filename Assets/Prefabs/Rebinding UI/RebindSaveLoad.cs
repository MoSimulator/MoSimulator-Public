using UnityEngine;
using UnityEngine.InputSystem;

namespace Prefabs.Rebinding_UI
{
    public class RebindSaveLoad : MonoBehaviour
    {
        public InputActionAsset actions;

        public void OnEnable()
        {
            var rebinds = PlayerPrefs.GetString("rebinds", null);
            if (!string.IsNullOrEmpty(rebinds))
                actions.LoadBindingOverridesFromJson(rebinds);
        }

        public void OnDisable()
        {
            var rebinds = actions.SaveBindingOverridesAsJson();
            PlayerPrefs.SetString("rebinds", rebinds);
        }
    }
}
