using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;

namespace Prefabs.Reefscape.Robots.Mods._4481.Remb
{
    public class RembrandtClimber : MonoBehaviour
    {

        [Header("Trigger Colliders")]
        public RembrandtClimberHelper Trigger1;

        [Header("Colliders to Enable")]
        public Collider[] hitBoxesToEnable;
        [Header("Colliders to Disable")]
        public Collider[] hitBoxesToDisable;

        private bool _firstTriggered;


        private void Start()
        {
            foreach (Collider elem in hitBoxesToEnable)
            {
                elem.enabled = false;
            }
            foreach (Collider elem in hitBoxesToDisable)
            {
                elem.enabled = true;
            }
        }

        public void NotifyTriggered(RembrandtClimberHelper helper, bool isTriggered)
        {
            if (helper == Trigger1)
                _firstTriggered = isTriggered;


            TryEnablingColliders();
            TryDisablingColliders();
        }

        private void TryEnablingColliders()
        {
            if (_firstTriggered)
            {
                foreach (Collider elem in hitBoxesToEnable)
                {
                    elem.enabled = true;
                }
            }
            else
            {
                foreach (Collider elem in hitBoxesToEnable)
                {
                    elem.enabled = false;
                }
            }
        }
        private void TryDisablingColliders()
        {
            if (_firstTriggered)
            {
                foreach (Collider elem in hitBoxesToDisable)
                {
                    elem.enabled = false;
                }
            }
            else
            {
                foreach (Collider elem in hitBoxesToDisable)
                {
                    elem.enabled = true;
                }
            }
        }
    }
}