using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MoSimLib
{
    #region Bumpers
    
    [Serializable]
    public enum VanityType
    {
        NoAllianceColors,
        CustomMaterial,
        CustomColor
    }

    [Serializable]
    public struct BumperSettings
    {
        [Header("Bumper Materials")]
        [Tooltip("Array of MeshRenderer components for bumper coloring.")]
        public MeshRenderer[] bumperMeshes;

        [Tooltip("Which Material Index is the Bumper Color")]
        public int materialIndex;

        [Tooltip("Material to apply to bumpers when robot is on the blue alliance.")]
        public Material blueBumperMaterial;

        [Tooltip("Material to apply to bumpers when robot is on the red alliance.")]
        public Material redBumperMaterial;

        [Header("Vanity")]
        public VanityBumpers Vanity;
    }
    
    [Serializable]
    public struct VanityBumpers
    {
        [Tooltip("This allows the player to not use either Red nor Blue Bumpers in Singleplayer")]
        public bool AllowVanity;
        
        [Tooltip("The type of Vanity Bumpers to apply")]
        public VanityType VanityType;
        
        // Based on VanityType
        [Tooltip("Custom Color to apply to bumpers")]
        public Color VanityColor;
        
        // Based on VanityType
        [Tooltip("Custom Material to apply to bumpers")]
        public Material VanityMaterial;
        
        [Tooltip("Game Objects to Disable when player is using 'Vanity Mode'")]
        public GameObject[] DisableOnVanity;
        
        [Tooltip("Game Objects to Enable when player is using 'Vanity Mode'")]
        public GameObject[] EnableOnVanity;
    }
    
    #endregion

    #region RSLSettings

    [Serializable]
    public struct RSLSettings
    {
        public MeshRenderer RSLMesh;
        public int materialIndex;
        public Material rslOnMaterial;
        public Material rslOffMaterial;
    }
    
    #endregion

    #region SOTM Settings

    [Serializable]
    public struct SOTMSettings
    {
        [Header("Turret + SOTM")]
        [SerializeField, Min(0f)] public float timeOfFlight;
        [SerializeField] public float velocityDampeningValue;
        [SerializeField, Range(0f, 1f)] public float shootingDriveMultiplier;
    }
    
    #endregion
}