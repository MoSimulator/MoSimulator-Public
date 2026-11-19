using UnityEngine;

namespace Robots
{
    public class RobotCenterOfMassVisual : MonoBehaviour
    {
        [SerializeField] private Color gizmoColor = Color.blue;
        [SerializeField] private float gizmoSize = 0.05f;

        private Vector3 _robotCenterOfMass;
        
        private Rigidbody[] _rigidbodies;
        
        private void Start()
        {
            _rigidbodies = GetComponentsInChildren<Rigidbody>();
        }

        private void OnDrawGizmos()
        {
            if (_rigidbodies == null || _rigidbodies.Length == 0)
            {
                return;
            }
            
            Gizmos.color = Color.yellow;
            
            var robotCenterOfMass = transform.position;
            
            foreach (var rb in _rigidbodies)
            {
                if (rb != null)
                {
                    robotCenterOfMass += rb.worldCenterOfMass;
                }
            }
            
            robotCenterOfMass /= _rigidbodies.Length;
            
            Gizmos.DrawSphere(robotCenterOfMass, 0.05f);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, robotCenterOfMass);

            ComputeRobotCenterOfMass();
            
            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(_robotCenterOfMass, gizmoSize);
            
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _robotCenterOfMass);
        }

        private void ComputeRobotCenterOfMass()
        {
            var rigidbodies = GetComponentsInChildren<Rigidbody>();

            if (rigidbodies.Length == 0)
            {
                _robotCenterOfMass = transform.position;
                return;
            }

            var totalMass = 0f;
            var weightedSum = Vector3.zero;

            foreach (var rb in rigidbodies)
            {
                var mass = rb.mass;
                var worldCenterOfMass = rb.worldCenterOfMass;
                
                weightedSum += worldCenterOfMass * mass;
                totalMass += mass;
            }
            
            _robotCenterOfMass = totalMass > 0 ? weightedSum / totalMass : transform.position;
        }
    }
}