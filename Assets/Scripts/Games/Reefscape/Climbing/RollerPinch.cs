using RobotFramework.Components;
using UnityEngine;

namespace Robots.Climbing
{
    public class Roller : MonoBehaviour
    {
        [SerializeField] private GenericAnimationJoint clicker1;
        [SerializeField] private GenericAnimationJoint clicker2;
        [SerializeField] private GenericAnimationJoint clicker3;
        [SerializeField] private GenericAnimationJoint clicker4;
        // Start is called before the first frame update
        void Start()
        {
        
        }

        // Update is called once per frame
        void FixedUpdate()
        {
            clicker1.SpringLoaded().AllowedDirection(1).RotationSpeed(360);
            clicker2.SpringLoaded().AllowedDirection(1).RotationSpeed(360);
        
            clicker3.SpringLoaded().RotationSpeed(360);
            clicker4.SpringLoaded().RotationSpeed(360);
        }
    }
}
