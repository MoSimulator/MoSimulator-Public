using System;
using MoSimLib;
using UnityEngine;

namespace RobotFramework.Controllers.PidSystems
{
    [Serializable]
    public class PIDController
    {
        public enum DerivativeMeasurement {
            Velocity,
            ErrorRateOfChange
        }

        /// <summary>
        /// The P value of the PID
        /// </summary>
        public Fixed32 proportionalGain = new Fixed32(Fixed32.DefaultScale, 0);
        /// <summary>
        /// The I gain of the PID
        /// </summary>
        public Fixed32 integralGain = new Fixed32(Fixed32.DefaultScale, 0);
        /// <summary>
        /// The D gain of the PID
        /// </summary>
        public Fixed32 derivativeGain = new Fixed32(Fixed32.DefaultScale, 0);

        /// <summary>
        /// Minimum output value for the PID
        /// </summary>
        public Fixed32 outputMin = new Fixed32(Fixed32.DefaultScale, -1);
        /// <summary>
        /// Maximum Output value for the PID
        /// </summary>
        public Fixed32 outputMax = new Fixed32(Fixed32.DefaultScale, 1);
        public Fixed32 integralSaturation = new Fixed32(Fixed32.DefaultScale, 0);
        public DerivativeMeasurement derivativeMeasurement;

        public Fixed32 valueLast = new Fixed32(Fixed32.DefaultScale, 0);
        public Fixed32 errorLast = new Fixed32(Fixed32.DefaultScale, 0);
        public Fixed32 integrationStored = new Fixed32(Fixed32.DefaultScale, 0);
        public Fixed32 velocity = new Fixed32(Fixed32.DefaultScale, 0);
        public bool derivativeInitialized;
        public Fixed32 dOutput =  new Fixed32(Fixed32.DefaultScale, 0);

        /// <summary>
        /// Resets the PID controller
        /// </summary>
        public MoSimResult<MoSimLib.Void> ResetController() {
            var version = MoSim.ActiveVersion;
            MigrateLegacyFixed32Values();
            ResetControllerV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ResetControllerV1() {
            derivativeInitialized = false;
        }

        public static Fixed32 ToFixedDeltaTime(float _dt)
        {
            var version = MoSim.ActiveVersion;
            return ToFixedDeltaTimeV1(_dt);
        }

        private static Fixed32 ToFixedDeltaTimeV1(float _dt)
        {
            float minDt = 1f / Fixed32.ScaleFactor(Fixed32.DefaultScale);
            return new Fixed32(Fixed32.DefaultScale, Mathf.Max(_dt, minDt));
        }

        /// <summary>
        /// Update the PID in linear mode
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="currentValue"></param>
        /// <param name="targetValue"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public MoSimResult<float> UpdateLinear(float dt, float currentValue, float targetValue) {
            var version = MoSim.ActiveVersion;
            MigrateLegacyFixed32Values();
            return MoSimResult<float>.Success(UpdateLinearV1(dt, currentValue, targetValue));
        }

        private float UpdateLinearV1(float dt, float currentValue, float targetValue) {
            if (dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
            Fixed32 dT = ToFixedDeltaTimeV1(dt);
            Fixed32 cValue = new Fixed32(Fixed32.DefaultScale, currentValue);
            Fixed32 tValue = new Fixed32(Fixed32.DefaultScale, targetValue);
            
            Fixed32 error = tValue - cValue;

            Fixed32 P = proportionalGain * error;

            integrationStored = Fixed32.Clamp(integrationStored + error * dT, -integralSaturation, integralSaturation);
            Fixed32 I = integralGain * integrationStored;

            Fixed32 errorRateOfChange = (error - errorLast) / dT;
            errorLast = error;

            Fixed32 valueRateOfChange = (cValue - valueLast) / dT;
            valueLast = cValue;
            velocity = valueRateOfChange;

            Fixed32 deriveMeasure = 0;

            if (derivativeInitialized) {
                if (derivativeMeasurement == DerivativeMeasurement.Velocity)
                    deriveMeasure = -valueRateOfChange;
                else
                    deriveMeasure = errorRateOfChange;
            }
            else {
                derivativeInitialized = true;
            }

            Fixed32 D = derivativeGain * deriveMeasure;
            
            dOutput = D;

            Fixed32 pd = Fixed32.Clamp(P + D, outputMin, outputMax);

            Fixed32 result = pd + I;

            return Fixed32.Clamp(result, outputMin - integralSaturation, outputMax + integralSaturation);
        }

        /// <summary>
        /// Calculates the angle difference
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public MoSimResult<Fixed32> AngleDifference(float a, float b) {
            var version = MoSim.ActiveVersion;
            return MoSimResult<Fixed32>.Success(AngleDifferenceV1(a, b));
        }

        private Fixed32 AngleDifferenceV1(float a, float b) {
            float valA = (float)a; 
            float valB = (float)b;
    
            float diff = (valA - valB + 540f) % 360f - 180f;
    
            return new Fixed32(Fixed32.DefaultScale, diff);
        }

        /// <summary>
        /// Updates the angle value (for angular mode)
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="currentAngle"></param>
        /// <param name="targetAngle"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public MoSimResult<float> UpdateAngle(float dt, float currentAngle, float targetAngle) {
            var version = MoSim.ActiveVersion;
            MigrateLegacyFixed32Values();
            return MoSimResult<float>.Success(UpdateAngleV1(dt, currentAngle, targetAngle));
        }

        private void MigrateLegacyFixed32Values()
        {
            proportionalGain = Fixed32.MigrateLegacyBinary16(proportionalGain);
            integralGain = Fixed32.MigrateLegacyBinary16(integralGain);
            derivativeGain = Fixed32.MigrateLegacyBinary16(derivativeGain);
            outputMin = Fixed32.MigrateLegacyBinary16(outputMin);
            outputMax = Fixed32.MigrateLegacyBinary16(outputMax);
            integralSaturation = Fixed32.MigrateLegacyBinary16(integralSaturation);
            valueLast = Fixed32.MigrateLegacyBinary16(valueLast);
            errorLast = Fixed32.MigrateLegacyBinary16(errorLast);
            integrationStored = Fixed32.MigrateLegacyBinary16(integrationStored);
            velocity = Fixed32.MigrateLegacyBinary16(velocity);
            dOutput = Fixed32.MigrateLegacyBinary16(dOutput);
        }

        private float UpdateAngleV1(float dt, float currentAngle, float targetAngle) {
            if (dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
            Fixed32 dT = ToFixedDeltaTimeV1(dt);
            Fixed32 cAngle = new Fixed32(Fixed32.DefaultScale, currentAngle);
            Fixed32 tAngle = new Fixed32(Fixed32.DefaultScale, targetAngle);
            
            Fixed32 error = AngleDifferenceV1(tAngle, cAngle);

            Fixed32 P = proportionalGain * error;

            integrationStored = Fixed32.Clamp(integrationStored + error * dT, -integralSaturation, integralSaturation);
            Fixed32 I = integralGain * integrationStored;

            Fixed32 errorRateOfChange = AngleDifferenceV1(error, errorLast) / dT;
            errorLast = error;

            Fixed32 valueRateOfChange = AngleDifferenceV1(cAngle, valueLast) / dT;
            valueLast = cAngle;
            velocity = valueRateOfChange;

            Fixed32 deriveMeasure = 0;

            if (derivativeInitialized) {
                if (derivativeMeasurement == DerivativeMeasurement.Velocity)
                    deriveMeasure = -valueRateOfChange;
                else
                    deriveMeasure = errorRateOfChange;
            }
            else {
                derivativeInitialized = true;
            }

            Fixed32 D = derivativeGain * deriveMeasure;

            dOutput = D;

            Fixed32 pd = Fixed32.Clamp(P + D, outputMin, outputMax);

            Fixed32 result = pd + I;

            return Fixed32.Clamp(result, outputMin - integralSaturation, outputMax+ integralSaturation);
        }
    }
}
