using System;
using UnityEngine;

namespace GameSystems.Management
{
    public class RebuiltFuelPreloadManager : MonoBehaviour
    {
        [Header("Removable Fuel")]
        [SerializeField] private GameObject[] removableFuel = Array.Empty<GameObject>();
        
        [Header("Fuel Hierarchy")]
        [SerializeField] private Transform centerFuelParent; 
        
        public Transform CenterFuelParent => centerFuelParent;

        public void RemoveFuel(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount of fuel to remove cannot be negative.");
            }

            if (amount > removableFuel.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, $"Cannot remove {amount} fuel objects because only " + $"{removableFuel.Length} are configured.");
            }
            
            for (int i = 0; i < amount; i++)
            {
                var fuel = removableFuel[i];

                if (fuel == null)
                {
                    throw new InvalidOperationException($"Removable fuel element {i} is not assigned.");
                }
                
                Destroy(removableFuel[i]);
            }
        }
    }
}