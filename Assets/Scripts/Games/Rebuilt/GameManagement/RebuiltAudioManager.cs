using MoSimCore.BaseClasses.GameManagement.AudioManagement;
using UnityEngine;

namespace Games.Rebuilt.GameManagement
{
    public class RebuiltAudioManager : BaseAudioManager
    {
        [SerializeField] private AudioClip matchStartClip;
        [SerializeField] private AudioClip teleopStartClip;
        [SerializeField] private AudioClip endgameStartClip;
        [SerializeField] private AudioClip matchEndClip;
        [SerializeField] private AudioClip shiftSwitch;


        protected override AudioClip MatchStartClip => matchStartClip;
        protected override AudioClip TeleopStartClip => teleopStartClip;
        protected override AudioClip EndgameStartClip => endgameStartClip;
        protected override AudioClip MatchEndClip => matchEndClip;
        public void PlayShiftSwitch()
        {
            audioSource.PlayOneShot(shiftSwitch);
        }
    }
}