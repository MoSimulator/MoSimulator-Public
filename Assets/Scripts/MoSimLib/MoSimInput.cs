using UnityEngine.InputSystem;

namespace MoSimLib
{
    
    public class MoSimInput
    {
        private InputAction action;
        private bool triggered;
        private bool wasPressed;

        public MoSimInput(InputAction action)
        {
            this.action = action;
            triggered = false;
            wasPressed = false;
        }

        public bool IsPressed()
        {
            return action.IsPressed();
        }
        
        public static implicit operator bool(MoSimInput input)
        {
            return input.IsPressed();
        }

        public bool WasTriggered()
        {
            return triggered;
        }

        public void ChangeTriggered(bool value)
        {
            triggered = value;
        }

        public void ChangeWasPressed(bool value)
        {
            wasPressed = value;
        }

        public bool GetWasPressed()
        {
            return wasPressed;
        }
    }
}