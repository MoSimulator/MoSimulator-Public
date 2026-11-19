using RobotFramework.Components;
using MoSimLib;

namespace RobotFramework.Interfaces
{
    public interface IRoller
    {
        MoSimResult<bool> FlipRpm();
        MoSimResult<MoSimLib.Void> Stop();
        MoSimResult<bool> OverrideRpm(float rpm);
        MoSimResult<MoSimLib.Void> SetRpm(float rpm);

        /// <summary>
        /// Scales whatever speed the roller would command this frame by 1/divider, for exactly
        /// one frame, without the caller knowing or touching the setpoint. Reverts the first
        /// frame it is not called.
        /// <para>
        /// Fills the gap between <see cref="Stop"/> and <see cref="FlipRpm"/>: both of those pin
        /// the surface speed to a fixed value, and because the friction model derives available
        /// motor torque from the commanded tip speed, a stopped roller still grips at full stall
        /// torque. Dividing lets you ease the surface speed — and with it the grip — instead of
        /// slamming it to an endpoint.
        /// </para>
        /// Composes with an override already staged this frame, so Stop() then DivideSpeed(2)
        /// stays stopped and FlipRpm() then DivideSpeed(2) runs backwards at half speed.
        /// </summary>
        /// <param name="divider">Non-zero. Negative values reverse as well as scale.</param>
        /// <returns>False if the divider was zero (or near enough to blow up), in which case nothing changed.</returns>
        MoSimResult<bool> DivideSpeed(float divider);

        MoSimResult<IRoller> GetRoller(int id);

        MoSimResult<MoSimLib.Void> ChangeMotor(InteractionRoller.RollerMotor selectedMotor);
    }
}
