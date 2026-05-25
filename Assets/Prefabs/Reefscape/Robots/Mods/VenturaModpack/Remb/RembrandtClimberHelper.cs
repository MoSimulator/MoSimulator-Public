using UnityEngine;
using UnityEngine.Serialization;

namespace Prefabs.Reefscape.Robots.Mods._4481.Remb
{
    public class RembrandtClimberHelper : MonoBehaviour
    {

        public RembrandtClimber mainClimber;

        private void OnTriggerEnter(Collider other)
        {
            if (other.tag == "Cage")
            {
                mainClimber.NotifyTriggered(this, true);
            }
            else
            {
                mainClimber.NotifyTriggered(this, false);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            mainClimber.NotifyTriggered(this, false);
        }
    }
}