using System;
using System.Collections;
using System.Linq;
using MoSimLib;
using UnityEngine;

namespace RobotFramework.Components
{
    [Serializable]
    public class Interpolation
    {
        [SerializeField] public DistanceValue[] interpolationMap;
        
        private DistanceValue[] _sortedCache;

        [Header("Debug")]
        [SerializeField] public float Distance = 0;

        [SerializeField] public float Output = 0;
        
        private DistanceValue[] _rawSnapshot;

        public Interpolation(MonoBehaviour runner)
        {
            InitializeCache();
        }
        
        private void UpdateTable(DistanceValue[] newData)
        {
            if (!IsDirty(interpolationMap)) return;
            if (_sortedCache == null || _sortedCache.Length != newData.Length)
            {
                _sortedCache = new DistanceValue[newData.Length];
            }
        
            Array.Copy(newData, _sortedCache, newData.Length);
            Array.Sort(_sortedCache, (a, b) => a.distance.CompareTo(b.distance));
        }

        private bool IsDirty(DistanceValue[] newData)
        {
            if (_rawSnapshot == null || _rawSnapshot.Length != newData.Length) return true;

            return newData.Where((t, i) => !t.Equals(_rawSnapshot[i])).Any();
        }
        
        protected IEnumerator UpdateLoop()
        {
            while (true)
            {
                UpdateTable(interpolationMap);
                yield return new WaitForFixedUpdate();
            }

            yield return null;
            // ReSharper disable once IteratorNeverReturns
        }
        
        protected void InitializeCache()
        {
            if (interpolationMap == null || interpolationMap.Length == 0)
            {
                _sortedCache = Array.Empty<DistanceValue>();
                return;
            }

            _sortedCache = new DistanceValue[interpolationMap.Length];
    
            Array.Copy(interpolationMap, _sortedCache, interpolationMap.Length);

            Array.Sort(_sortedCache, new DistanceComparer());
        }
        
        public MoSimResult<float> Interpolate(float currentDistance)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(InterpolateV1(currentDistance));
        }

        private float InterpolateV1(float currentDistance)
        {
            Distance = currentDistance;
            Output = Lookup(currentDistance);
            return Output;
        }

        /// <summary>
        /// Lerps between the two table points around <paramref name="currentDistance"/>.
        /// Outside the table it returns the closest edge point's value instead of extrapolating.
        /// </summary>
        private float Lookup(float currentDistance)
        {
            if (_sortedCache == null || _sortedCache.Length == 0) return 0f;

            int index = Array.BinarySearch(_sortedCache, new DistanceValue { distance = currentDistance }, new DistanceComparer());

            if (index >= 0) return _sortedCache[index].value;

            int nextIndex = ~index;

            if (nextIndex == 0) return _sortedCache[0].value;
            if (nextIndex >= _sortedCache.Length) return _sortedCache[^1].value;

            var lower = _sortedCache[nextIndex - 1];
            var upper = _sortedCache[nextIndex];
            float t = (currentDistance - lower.distance) / (upper.distance - lower.distance);
            return Mathf.Lerp(lower.value, upper.value, t);
        }
    }
    
    [Serializable]
    public struct DistanceValue : IEquatable<DistanceValue>
    {
        public float distance;
        public float value;
        
        public bool Equals(DistanceValue other)
        {
            // Compare the fields directly
            return distance.Equals(other.distance) && value.Equals(other.value);
        }
        
        public override bool Equals(object obj) => obj is DistanceValue other && Equals(other);
        
        public override int GetHashCode() => HashCode.Combine(distance, value);

        public static bool operator ==(DistanceValue left, DistanceValue right) => left.Equals(right);
        public static bool operator !=(DistanceValue left, DistanceValue right) => !left.Equals(right);
    }
    
    public struct DistanceComparer : System.Collections.Generic.IComparer<DistanceValue>
    {
        public int Compare(DistanceValue x, DistanceValue y) => x.distance.CompareTo(y.distance);
    }
}
