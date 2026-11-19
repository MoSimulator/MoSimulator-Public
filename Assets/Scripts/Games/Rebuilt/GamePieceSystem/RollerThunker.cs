using MoSimLib;
using RobotFramework.Components;
using UnityEngine;
using UnityEngine.Audio;

public class RollerThunker : MonoBehaviour
{
    [SerializeField] private LayerMask gamePieceLayers;
    [SerializeField] private AudioClip audioClip;
    [SerializeField] private float pitch = 1;
    [SerializeField] private float volume = 0.25f;
    private AudioSource audioSource;

    private InteractionRoller _roller;
    private bool _useRoller;

    private void Start()
    {
        _roller = GetComponent<InteractionRoller>();
        _useRoller = _roller != null;
        
        audioSource = Utils.TryGetAddComponent<AudioSource>(gameObject);
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.clip = audioClip;
        audioSource.outputAudioMixerGroup = Resources.Load<AudioMixerGroup>("MainAudioMixer").audioMixer.FindMatchingGroups("RobotSounds")[0];
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsGamePiece(other) || (_useRoller && Mathf.Abs(_roller.realRpm) < 0.1f)) return;
        audioSource.Play();
    }
    
    private bool IsGamePiece(Collider other)
    {
        if (gamePieceLayers == 0) return true;
        return (gamePieceLayers.value & (1 << other.gameObject.layer)) != 0;
    }

    private void Update()
    {
        audioSource.pitch = pitch;
        audioSource.volume = volume;
    }
}
