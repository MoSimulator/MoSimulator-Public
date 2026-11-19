using System;
using System.Collections.Generic;
using System.Linq;
using Games.Rebuilt.Robots;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using UnityEngine;

namespace Games.Rebuilt.Components
{
    [Serializable]
    public class RebuiltInterpolation : Interpolation
    {
        [Header("Overrides")]
        [SerializeField, Tooltip("Uses the center of the robot if not set")] public Transform transformOverride;

        private Vector3 _targetPosition;
        
        private Dictionary<DefaultMap, Transform> _defaultMap;
        private Dictionary<DefaultPassMap, Transform> _defaultPassMap;

        private RebuiltRobotBase _base;
        
        public RebuiltInterpolation(RebuiltRobotBase Base, DistanceValue[] initialMap) : base(Base)
        {
            this._base = Base;
            this.interpolationMap = initialMap;
        }

        public MoSimResult<MoSimLib.Void> Initialize(RebuiltRobotBase Base)
        {
            var version = MoSim.ActiveVersion;
            InitializeV1(Base);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void InitializeV1(RebuiltRobotBase Base)
        {
            _defaultMap = new Dictionary<DefaultMap, Transform>();
            
            this._base = Base;
            
            base.InitializeCache();

            Base.StartCoroutine(UpdateLoop());
            
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

        public MoSimResult<float> Interpolate()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(InterpolateV1());
        }

        private float InterpolateV1()
        {
            float output = base.Interpolate(GetDistance());
            return output;
        }

        private float GetDistance()
        {
           return Vector3.Distance(transformOverride == null ? _base.transform.position : transformOverride.position, _targetPosition);
        }
        
        /// <summary>
        /// use a default map location
        /// </summary>
        /// <param name="map"></param>
        public MoSimResult<MoSimLib.Void> SetDefaultMap(DefaultMap map)
        {
            var version = MoSim.ActiveVersion;
            SetDefaultMapV1(map);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void SetDefaultMapV1(DefaultMap map)
        {
            _targetPosition = _defaultMap[map].position;
        }

        /// <summary>
        /// Use closest pose on the default Map
        /// </summary>
        public MoSimResult<MoSimLib.Void> SetPassMapClosest()
        {
            var version = MoSim.ActiveVersion;
            SetPassMapClosestV1();
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void SetPassMapClosestV1()
        {
            var minValue = Mathf.Infinity;
            Vector3 closest = new Vector3();
            foreach (var pose in _defaultPassMap.Values)
            {
                var local = _base.transform.InverseTransformPoint(pose.position);
                var mag = local.sqrMagnitude;
                if (mag < minValue)
                {
                    minValue = mag;
                    closest = pose.position;
                }
            }
            
            _targetPosition = closest;
        }

        /// <summary>
        /// use closest value on custom map
        /// </summary>
        /// <param name="map"></param>
        public MoSimResult<MoSimLib.Void> SetClosestCustomMap(Dictionary<string, Vector3> map)
        {
            var version = MoSim.ActiveVersion;
            SetClosestCustomMapV1(map);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void SetClosestCustomMapV1(Dictionary<string, Vector3> map)
        {
            var minValue = Mathf.Infinity;
            Vector3 closest = new Vector3();
            foreach (var id in map.Keys)
            {
                var local = _base.transform.InverseTransformPoint(map[id]);
                var mag = local.sqrMagnitude;
                if (mag < minValue)
                {
                    minValue = mag;
                    closest = local;
                }
            }
            
            _targetPosition = closest;
        }
        
        /// <summary>
        /// set a custom target value
        /// </summary>
        /// <param name="target"></param>
        public MoSimResult<MoSimLib.Void> SetCustomTarget(Vector3 target)
        {
            var version = MoSim.ActiveVersion;
            SetCustomTargetV1(target);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void SetCustomTargetV1(Vector3 target)
        {
            _targetPosition = target;
        }

        public MoSimResult<Vector3> GetCurrentTarget()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<Vector3>.Success(GetCurrentTargetV1());
        }

        private Vector3 GetCurrentTargetV1()
        {
            return _targetPosition;
        }
        
        
        
    }
}
