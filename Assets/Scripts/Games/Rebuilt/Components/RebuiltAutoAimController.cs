using System.Collections.Generic;
using System.Linq;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

namespace Games.Rebuilt.Components
{
    public class RebuiltAutoAimController : AutoAim
    {
        [Header("Target")]
        [SerializeField] private Vector3 targetPosition;
        [SerializeField] private AimControl aimControl = AimControl.Profiled;
        
        private Dictionary<DefaultMap, Transform> _defaultMap;
        private Dictionary<DefaultPassMap, Transform> _defaultPassMap;

        private RobotBase Base;

        protected override void Start()
        {
            base.Start();

            Base = GetComponent<RobotBase>();
            _defaultMap = new Dictionary<DefaultMap, Transform>();
            
            var foundTargets = Utils.FindGameObjectsOnLayer("AutoAngleNodes");

            foreach (var target in foundTargets.ToList()
                         .Where(
                             target => 
                             (target.name.ToLower().Contains("blue") && Base.Alliance == Alliance.Red) 
                             || 
                             (target.name.ToLower().Contains("red") && Base.Alliance == Alliance.Blue)
                         )
                     )
            {
                foundTargets.Remove(target);
            }
            foreach (var target in foundTargets)
            {
                _defaultMap.Add(Utils.GetStringToEnum<DefaultMap>(target.name), target.transform); 
            }
            
            _defaultPassMap = new Dictionary<DefaultPassMap, Transform>();

            foreach (var target in _defaultMap.Where(target => target.Key != DefaultMap.Hub))
            {
                _defaultPassMap.Add(Utils.GetStringToEnum<DefaultPassMap>(target.Key.ToString()), target.Value);
            }
        }

        public void Aim()
        {
            AimAt(targetPosition.x, targetPosition.z, 0f, aimControl);
        }

        public void Aim(float theta, float ff)
        {
            AimRotation(theta, ff, aimControl);
        }

        /// <summary>
        /// use a default map location
        /// </summary>
        /// <param name="map"></param>
        public void SetDefaultMap(DefaultMap map)
        {
            targetPosition = _defaultMap[map].position;
        }

        /// <summary>
        /// Use closest pose on the default Map
        /// </summary>
        public void SetPassMapClosest()
        {
            var minValue = Mathf.Infinity;
            Vector3 closest = new Vector3();
            foreach (var pose in _defaultPassMap.Values)
            {
                var local = transform.InverseTransformPoint(pose.position);
                var mag = local.sqrMagnitude;
                if (mag < minValue)
                {
                    minValue = mag;
                    closest = pose.position;
                }
            }
            
            targetPosition = closest;
        }

        /// <summary>
        /// use closest value on custom map
        /// </summary>
        /// <param name="map"></param>
        public void SetClosestCustomMap(Dictionary<string, Vector3> map)
        {
            var minValue = Mathf.Infinity;
            Vector3 closest = new Vector3();
            foreach (var id in map.Keys)
            {
                var local = transform.InverseTransformPoint(map[id]);
                var mag = local.sqrMagnitude;
                if (mag < minValue)
                {
                    minValue = mag;
                    closest = local;
                }
            }
            
            targetPosition = closest;
        }
        
        /// <summary>
        /// set a custom target value
        /// </summary>
        /// <param name="target"></param>
        public void SetCustomTarget(Vector3 target)
        {
            targetPosition = target;
        }

        public Vector3 GetCurrentTarget()
        {
            return targetPosition;
        }
    }

    public enum DefaultMap
    {
        Hub,
        LeftPass,
        RightPass,
    }

    public enum DefaultPassMap
    {
        LeftPass,
        RightPass,
    }
}
