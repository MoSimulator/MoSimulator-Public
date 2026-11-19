using System.Collections;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Games.Rebuilt.GameManagement
{
    public class RebuiltTimerManager : BaseTimerManager
    {
        protected override float MatchDuration => 160;
        protected override float TeleopStartTime => 140;
        protected override float EndgameStartTime => 30;
        
        protected override void StartTeleopTransition()
        {
            StartCoroutine(HandleTeleopTransition());
        }
        
        private IEnumerator HandleTeleopTransition()
        {
            PauseTimer();
            Timer = TeleopStartTime;
            UpdateTimerText();
            CurrentRobotState = RobotState.Disabled;
            InvokeAutoEnd();

            yield return new WaitForSeconds(3f);
            
            CurrentGameState = GameState.Teleop;
            CurrentRobotState = RobotState.Enabled;
            ResumeTimer();
            InvokeTeleopStart();
            InvokeGameStateChange();
        }
    }
}