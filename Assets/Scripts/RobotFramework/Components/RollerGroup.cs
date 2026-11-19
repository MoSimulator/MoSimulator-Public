using System;
using MoSimLib;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Components
{
    public class RollerGroup: MonoBehaviour, IRoller
    {
        [SerializeField] private RollerInstance[] rollers;
        
        public MoSimResult<MoSimLib.Void> SetRpm(float rpm)
        {
            var version = MoSim.ActiveVersion;
            SetRpmV1(rpm);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetRpmV1(float rpm)
        {
            foreach (RollerInstance r in rollers)
            {
                var setRpm = r.flipped ? -rpm : rpm;
                r.Roller.SetRpm(setRpm * r.Multiplier);
            }
        }
        
        public MoSimResult<bool> OverrideRpm(float rpm)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(OverrideRpmV1(rpm));
        }

        private bool OverrideRpmV1(float rpm)
        {
            bool output = true;
            foreach (RollerInstance r in rollers)
            {
                var setRpm = r.flipped ? -rpm : rpm;
                var o = r.Roller.OverrideRpm(setRpm * r.Multiplier);
                if (!o) output = false;
            }
            return output;
        }
        
        public MoSimResult<bool> FlipRpm()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(FlipRpmV1());
        }

        private bool FlipRpmV1()
        {
            bool output = true;
            foreach (RollerInstance r in rollers)
            {
                var o = r.Roller.FlipRpm();
                if (!o) output = false;
            }
            return output;
        }

        public MoSimResult<MoSimLib.Void> Stop()
        {
            var version = MoSim.ActiveVersion;
            StopV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void StopV1()
        {
            foreach (RollerInstance r in rollers)
            {
                r.Roller.Stop();
            }
        }

        /// <summary>
        /// Divide every member's speed for one frame. The divider is a ratio, so per-roller
        /// multipliers and flips are left alone — each roller keeps its relative speed.
        /// </summary>
        public MoSimResult<bool> DivideSpeed(float divider)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(DivideSpeedV1(divider));
        }

        private bool DivideSpeedV1(float divider)
        {
            bool output = true;
            foreach (RollerInstance r in rollers)
            {
                var o = r.Roller.DivideSpeed(divider);
                if (!o) output = false;
            }
            return output;
        }
        
        public MoSimResult<MoSimLib.Void> SetAllMp(float mp)
        {
            var version = MoSim.ActiveVersion;
            SetAllMpV1(mp);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetAllMpV1(float mp)
        {
            foreach (RollerInstance r in rollers)
            {
                r.SetMp(mp);
            }
        }
        
        public MoSimResult<MoSimLib.Void> ChangeMotor(InteractionRoller.RollerMotor selectedMotor)
        {
            var version = MoSim.ActiveVersion;
            ChangeMotorV1(selectedMotor);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ChangeMotorV1(InteractionRoller.RollerMotor selectedMotor)
        {
            foreach (RollerInstance r in rollers)
            {
                r.Roller.ChangeMotor(selectedMotor);
            }
        }
        
        /// <summary>
        /// This returns an instance from id using the set array.
        /// SHOULD BE AVOIDED if possible 
        /// </summary>
        public MoSimResult<IRoller> GetRoller(int id)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<IRoller>.Success(GetRollerV1(id));
        }

        private IRoller GetRollerV1(int id)
        {
            return rollers[id].Roller;
        }

        public MoSimResult<bool> AtSpeed()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(AtSpeedV1());
        }

        private bool AtSpeedV1()
        {
            bool hasSpeedControlledRoller = false;
            foreach (RollerInstance r in rollers)
            {
                PvffController pvffController = r.GetPvffController();
                if (pvffController != null)
                {
                    hasSpeedControlledRoller = true;
                    if (!pvffController.IsAtSpeed)
                    {
                        return false;
                    }

                    continue;
                }

                if (r.GetFlywheel() == null)
                {
                    InteractionRoller interactionRoller = r.GetInteractionRoller();
                    if (interactionRoller == null)
                    {
                        continue;
                    }

                    hasSpeedControlledRoller = true;
                    if (!interactionRoller.IsAtSpeed)
                    {
                        return false;
                    }

                    continue;
                }

                hasSpeedControlledRoller = true;
                if (!r.GetFlywheel().IsAtSpeed)
                {
                    return false;
                }
            }

            return hasSpeedControlledRoller;
        }

        public MoSimResult<float> GetAverageCurrentRpm()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(GetAverageCurrentRpmV1());
        }

        private float GetAverageCurrentRpmV1()
        {
            return GetAverageRpm(_roller => _roller.GetCurrentRpm() / _roller.Multiplier);
        }

        public MoSimResult<float> GetAverageTargetRpm()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(GetAverageTargetRpmV1());
        }

        private float GetAverageTargetRpmV1()
        {
            return GetAverageRpm(_roller => _roller.GetTargetRpm() / _roller.Multiplier);
        }

        public MoSimResult<float> GetAverageCommandedRpm()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<float>.Success(GetAverageCommandedRpmV1());
        }

        private float GetAverageCommandedRpmV1()
        {
            return GetAverageRpm(_roller => _roller.GetCommandedRpm()  / _roller.Multiplier);
        }

        private float GetAverageRpm(Func<RollerInstance, float?> _getRpm)
        {
            float total = 0f;
            int count = 0;
            foreach (RollerInstance roller in rollers)
            {
                float? rpm = _getRpm(roller);
                if (!rpm.HasValue)
                {
                    continue;
                }
                else
                {
                    rpm = roller.Multiplier;
                }

                total += roller.flipped ? -rpm.Value : rpm.Value;
                count++;
            }

            return count == 0 ? 0f : total / count;
        }

        [Serializable]
        public class RollerInstance
        {
            [SerializeField] private InteractionRoller IntRoller;
            [SerializeField] private GenericRoller GenRoller;
            [SerializeField] private PvffController PvffController;
            [SerializeField] private FlywheelBehaviour Flywheel;
            // ReSharper disable once UnassignedField.Global
            // ReSharper disable once NotNullOrRequiredMemberIsNotInitialized
            public IRoller Roller => GetRoller();
            [SerializeField] public float Multiplier = 1;
            public bool flipped;
            private IRoller GetRoller()
            {
                if (GenRoller) return GenRoller;
                if (PvffController) return PvffController;
                if (IntRoller) return IntRoller;
                return Flywheel;
            }

            public FlywheelBehaviour GetFlywheel() => Flywheel;
            public PvffController GetPvffController() => PvffController;
            public InteractionRoller GetInteractionRoller() => IntRoller;

            public float? GetCurrentRpm()
            {
                if (PvffController) return PvffController.CurrentRpm;
                if (Flywheel) return Flywheel.CurrentRpm;
                if (IntRoller) return IntRoller.realRpm;
                return null;
            }

            public float? GetTargetRpm()
            {
                if (PvffController) return PvffController.TargetRpm;
                if (Flywheel) return Flywheel.TargetRpm;
                if (IntRoller) return IntRoller.TargetRpm;
                return null;
            }

            public float? GetCommandedRpm()
            {
                if (PvffController) return PvffController.CommandedRpm;
                if (Flywheel) return Flywheel.CurrentRpm;
                if (IntRoller) return IntRoller.TargetRpm;
                return null;
            }

            public void SetMp(float mp)
            {
                Multiplier = mp;
            }
        }
        
    }
}
