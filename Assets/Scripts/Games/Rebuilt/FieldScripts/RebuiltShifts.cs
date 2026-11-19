using Games.Rebuilt.GameManagement;
using Games.Rebuilt.Scoring;
using MoSimCore.BaseClasses.GameManagement;
using UnityEngine;

namespace Games.Rebuilt.FieldScripts
{
    public class RebuiltShifts: MonoBehaviour
    {
        [SerializeField] private HubLights redLights;
        [SerializeField] private HubLights blueLights;
        [SerializeField] private Outpost redOutpost;
        [SerializeField] private Outpost blueOutpost;
        
        public static ActiveHub ActiveHub;

        public static ActiveHub WonAuto { get; private set; }
        public static ActiveHub LostAuto { get; private set; }
        public static bool IsShiftEndingSoon { get; private set; }
        public static float SecondsUntilNextShift { get; private set; }

        private const float ShiftEndWarningSeconds = 3f;

        private RebuiltAudioManager audioManager;
        
        private bool _shift2Played = false;
        private bool _shift3Played = false;
        private bool _shift4Played = false;
        private bool _shift5Played = false;

        private bool[] incrementCount;

        public static int ShiftNumber;
        
        private bool autonDecided;
        
        private bool _greenPlayed = false;
        private float _afterMatch = 0f;

        
        private void Start()
        {
            ActiveHub = new ActiveHub();
            audioManager = FindAnyObjectByType<RebuiltAudioManager>();
            ShiftNumber = 0;
            autonDecided = false;
            incrementCount = new bool[6];
            _greenPlayed = false;
            _afterMatch = 0f;
        }

        private void PlayShiftSound(ref bool played)
        {
            if (!played)
            {
                audioManager.PlayShiftSwitch();
                played = true;
            }
        }

        private void IncrementShiftCount(int id)
        {
            if (!incrementCount[id])
            {
                ShiftNumber++;
                incrementCount[id] = true;
            }
        }

        private void Update()
        {
            var currentTime = BaseGameManager.Instance.Timer;

            UpdateShiftWarning(currentTime);
            
            if (currentTime <= 0)
            {
                _afterMatch += Time.deltaTime;

                HubLights.Shift postMatch;
                
                if (_afterMatch < 5f)
                    postMatch = HubLights.Shift.WHITE; 
                else if (_afterMatch < 8f)
                    postMatch = HubLights.Shift.INACTIVE; 
                else if (_afterMatch < 15f)
                    postMatch = HubLights.Shift.PURPLE;
                else
                    postMatch = HubLights.Shift.GREEN;
                
                redLights.SetShift(postMatch);
                blueLights.SetShift(postMatch);
                redOutpost.SetHumanPlayerActive(false);
                blueOutpost.SetHumanPlayerActive(false);
                
                return; 
            }

            //Auto
            if (currentTime >= 140)
            {
                ActiveHub = ActiveHub.Both;
                
                redLights.SetShift(HubLights.Shift.ACTIVE);
                blueLights.SetShift(HubLights.Shift.ACTIVE);
                redOutpost.SetHumanPlayerActive(true);
                blueOutpost.SetHumanPlayerActive(true);
                
            } else if (currentTime >= 130) //transition
            {
                DecideAutonWinner();
                ActiveHub = ActiveHub.Both;

                if (WonAuto == ActiveHub.Red)
                {
                    redLights.SetShift(IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.TRANSITION_WON);
                    blueLights.SetShift(HubLights.Shift.ACTIVE);
                }
                else
                {
                    blueLights.SetShift(IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.TRANSITION_WON);
                    redLights.SetShift(HubLights.Shift.ACTIVE);
                }
                
                redOutpost.SetHumanPlayerActive(true);
                blueOutpost.SetHumanPlayerActive(true);
                IncrementShiftCount(0);
            } 
            else if (currentTime >= 105) //shift 1
            {
                ActiveHub = LostAuto;
                PlayShiftSound(ref _shift2Played);
                redLights.SetShift(ActiveHub == ActiveHub.Red ? IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                blueLights.SetShift(ActiveHub == ActiveHub.Blue ? IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                redOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Red);
                blueOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Blue);
                IncrementShiftCount(1);
            } 
            else if (currentTime >= 80) //shift 2
            {
                ActiveHub = WonAuto;
                PlayShiftSound(ref _shift3Played);
                redLights.SetShift(ActiveHub == ActiveHub.Red ? IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                blueLights.SetShift(ActiveHub == ActiveHub.Blue ? IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                redOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Red);
                blueOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Blue);
                IncrementShiftCount(2);
            } 
            else if (currentTime >= 55) //shift 3
            {
                ActiveHub = LostAuto;
                PlayShiftSound(ref _shift4Played);
                redLights.SetShift(ActiveHub == ActiveHub.Red ? IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                blueLights.SetShift(ActiveHub == ActiveHub.Blue ? IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                redOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Red);
                blueOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Blue);
                IncrementShiftCount(3);
            } 
            else if (currentTime >= 30) //shift 4
            {
                ActiveHub = WonAuto;
                PlayShiftSound(ref _shift5Played);
                redLights.SetShift(ActiveHub == ActiveHub.Red ? HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                blueLights.SetShift(ActiveHub == ActiveHub.Blue ? HubLights.Shift.ACTIVE : HubLights.Shift.INACTIVE);
                redOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Red);
                blueOutpost.SetHumanPlayerActive(ActiveHub == ActiveHub.Blue);
                IncrementShiftCount(4);
            } 
            else //endgame and on
            {
                ActiveHub = ActiveHub.Both;
                
                redLights.SetShift(IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE);
                blueLights.SetShift(IsShiftEndingSoon ? HubLights.Shift.WARNING_PULSE : HubLights.Shift.ACTIVE);
                redOutpost.SetHumanPlayerActive(true);
                blueOutpost.SetHumanPlayerActive(true);
                
                IncrementShiftCount(5);
            }
        }

        private void UpdateShiftWarning(float currentTime)
        {
            float nextShiftTime = GetNextShiftTime(currentTime);
            SecondsUntilNextShift = Mathf.Max(0f, currentTime - nextShiftTime);
            IsShiftEndingSoon = SecondsUntilNextShift <= ShiftEndWarningSeconds;
        }

        private float GetNextShiftTime(float currentTime)
        {
            if (currentTime >= 140f) return 140f;
            if (currentTime >= 130f) return 130f;
            if (currentTime >= 105f) return 105f;
            if (currentTime >= 80f)  return 80f;
            if (currentTime >= 55f)  return 55f;
            if (currentTime >= 30f)  return 30f;
            return 0f;
        }

        private void DecideAutonWinner()
        {
            if (autonDecided) return;
            if (RebuiltScoreUI.redAutoFuelScore > RebuiltScoreUI.blueAutoFuelScore)
            {
                WonAuto = ActiveHub.Red;
                LostAuto = ActiveHub.Blue;
            }
            else if (RebuiltScoreUI.redAutoFuelScore < RebuiltScoreUI.blueAutoFuelScore)
            {
                WonAuto = ActiveHub.Blue;
                LostAuto = ActiveHub.Red;
            }
            else
            {
                bool redWinsTie = UnityEngine.Random.value > 0.5f;
    
                WonAuto = redWinsTie ? ActiveHub.Red : ActiveHub.Blue;
                LostAuto = redWinsTie ? ActiveHub.Blue : ActiveHub.Red;
            }
            
            autonDecided = true;
        }
    }

    public enum ActiveHub
    {
        Red,
        Blue,
        Both
    };
}