using System;

namespace MoSimLib
{
    public struct Fixed32
    {
        // Scale is the number of decimal places, not a count of binary fraction bits.
        // A value with Scale == 2 stores 10.02 as RawValue == 1002.
        public readonly int Scale;
        public const int DefaultScale = 6;
        public const int LegacyBinaryScale = 16;

        public long RawValue { get; private set; }

        public Fixed32(int scale) : this(scale, 0) { }

        public Fixed32(int scale, int wholeNumber)
        {
            ValidateScale(scale);
            this.Scale = scale;
            this.RawValue = wholeNumber * ScaleFactor(scale);
        }

        public Fixed32(int scale, float number)
        {
            ValidateScale(scale);
            this.Scale = scale;
            this.RawValue = (long)Math.Round(number * ScaleFactor(scale), MidpointRounding.AwayFromZero);
        }

        private Fixed32(int scale, long rawValue)
        {
            ValidateScale(scale);
            this.Scale = scale;
            this.RawValue = rawValue;
        }

        public int WholeNumber
        {
            get
            {
                return (int)(this.RawValue / ScaleFactor(this.Scale));
            }
        }

        public int Fraction
        {
            get
            {
                return (int)Math.Abs(this.RawValue % ScaleFactor(this.Scale));
            }
        }

        public static Fixed32 operator +(Fixed32 leftHandSide, Fixed32 rightHandSide)
        {
            NormalizeScales(ref leftHandSide, ref rightHandSide);
            leftHandSide.RawValue += rightHandSide.RawValue;

            return leftHandSide;
        }

        public static Fixed32 operator -(Fixed32 leftHandSide, Fixed32 rightHandSide)
        {
            NormalizeScales(ref leftHandSide, ref rightHandSide);
            leftHandSide.RawValue -= rightHandSide.RawValue;

            return leftHandSide;
        }

        public static Fixed32 operator *(Fixed32 leftHandSide, Fixed32 rightHandSide)
        {
            NormalizeScales(ref leftHandSide, ref rightHandSide);
            var result = leftHandSide.RawValue * rightHandSide.RawValue;

            leftHandSide.RawValue = result / ScaleFactor(leftHandSide.Scale);

            return leftHandSide;
        }

        public static Fixed32 operator /(Fixed32 leftHandSide, Fixed32 rightHandSide)
        {
            NormalizeScales(ref leftHandSide, ref rightHandSide);
            if (rightHandSide.RawValue == 0)
            {
                leftHandSide.RawValue = 0;
                return leftHandSide;
            }

            var result = (leftHandSide.RawValue * ScaleFactor(leftHandSide.Scale)) / rightHandSide.RawValue;

            leftHandSide.RawValue = result;

            return leftHandSide;
        }

        public static Fixed32 operator -(Fixed32 value)
        {
            return new Fixed32(value.Scale, -value.RawValue);
        }
        
        public static implicit operator Fixed32(float value)
        {
            const int defaultScale = 16; 
            return new Fixed32(defaultScale, value);
        }

        public static implicit operator float(Fixed32 value)
        {
            return (float)value.RawValue / ScaleFactor(value.Scale);
        }

        public static explicit operator double(Fixed32 number)
        {
            return (double)number.RawValue / ScaleFactor(number.Scale);
        }
        

        public static implicit operator int(Fixed32 number)
        {
            return number.WholeNumber;
        }


        public static implicit operator Fixed32(int number)
        {
            return new Fixed32(DefaultScale, number);
        }

        public override string ToString()
        {
            return ((double)this).ToString();
        }

        public static Fixed32 Clamp(Fixed32 value, Fixed32 min, Fixed32 max)
        {
            NormalizeScales(ref value, ref min);
            NormalizeScales(ref value, ref max);
            if (value.RawValue < min.RawValue) return min;
            if (value.RawValue > max.RawValue) return max;
            return value;
        }

        /// <summary>
        /// Converts a value stored by the pre-decimal Fixed32 implementation.
        /// That implementation used Scale 16 to mean RawValue / 65536.
        /// </summary>
        public static Fixed32 MigrateLegacyBinary16(Fixed32 value)
        {
            if (value.Scale != LegacyBinaryScale) return value;

            // The previous float constructor used Mathf.RoundToInt, so persisted
            // legacy constants always fit in Int32. Larger raw values were made by
            // this decimal implementation and must only be rescaled, not treated
            // as Q16 values a second time.
            if (value.RawValue > int.MaxValue || value.RawValue < int.MinValue)
            {
                var rescaledRawValue = (long)Math.Round(
                    (double)value.RawValue / ScaleFactor(LegacyBinaryScale - DefaultScale),
                    MidpointRounding.AwayFromZero);
                return new Fixed32(DefaultScale, rescaledRawValue);
            }

            var decimalRawValue = (long)Math.Round(
                (double)value.RawValue * ScaleFactor(DefaultScale) / (1 << LegacyBinaryScale),
                MidpointRounding.AwayFromZero);
            return new Fixed32(DefaultScale, decimalRawValue);
        }

        public static long ScaleFactor(int scale)
        {
            ValidateScale(scale);
            long factor = 1;
            for (var i = 0; i < scale; i++) factor *= 10;
            return factor;
        }

        private static void NormalizeScales(ref Fixed32 leftHandSide, ref Fixed32 rightHandSide)
        {
            var commonScale = Math.Max(leftHandSide.Scale, rightHandSide.Scale);
            leftHandSide = ConvertScale(leftHandSide, commonScale);
            rightHandSide = ConvertScale(rightHandSide, commonScale);
        }

        private static Fixed32 ConvertScale(Fixed32 value, int targetScale)
        {
            if (value.Scale == targetScale) return value;
            return new Fixed32(targetScale, value.RawValue * ScaleFactor(targetScale - value.Scale));
        }

        private static void ValidateScale(int scale)
        {
            if (scale < 0 || scale > 18)
                throw new ArgumentOutOfRangeException(nameof(scale), "Decimal scale must be between 0 and 18.");
        }
    }
}

//MIT License
//
// Copyright (c) 2019 Storm Muller
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
//     of this software and associated documentation files (the "Software"), to deal
//     in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
//     furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
//     copies or substantial portions of the Software.
//
//     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//     IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//     FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//     AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//     LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
