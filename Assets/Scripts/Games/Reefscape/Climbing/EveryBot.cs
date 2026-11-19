using RobotFramework.Components;
using UnityEngine;

namespace Robots.Climbing
{
    public class EveryBot : MonoBehaviour
    {
        [SerializeField] private GenericAnimationJoint animationJoint;
        [SerializeField] private GenericAnimationJoint animationJointTwo;

        // Start is called before the first frame update
        void Start()
        {
        
        }

        // Update is called once per frame
        void Update()
        {
        
        }

        private void FixedUpdate()
        {
            animationJoint.SpringLoaded();
            animationJointTwo.SpringLoaded();
        }
    }
}
