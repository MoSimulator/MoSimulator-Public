using System;
using MoSimLib;
using UnityEngine;

namespace RobotFramework.Components
{
    /// <summary>
    /// Smooths rigidbody motion using Kalman filtering for position and rotation.
    /// Reduces jitter and interpolates between physics frames for smooth visuals.
    /// Automatically attached to robots during initialization.
    /// </summary>
    public class CustomRigidbodyInterpolation : MonoBehaviour
    {
        private Rigidbody rb;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private bool isInterpolatingPosition = true;
        private bool isInterpolatingRotation = true;
        public float positionStiffness = 10f;
        public float positionDamping = 2f;
        public float rotationStiffness = 10f;
        public float rotationDamping = 2f;
        private Vector3 velocity;
        private Vector3 angularVelocity;
        public bool useFixedUpdate = true;
        private Joint joint;
        private Vector3 jointOffsetPosition;
        private Quaternion jointOffsetRotation;

        // Kalman filter variables for position
        private Vector3 positionEstimate;
        private MatrixF positionCovariance;
        public float positionProcessNoise = 0.02f; //tune
        public float positionMeasurementNoise = 0.1f; //tune

        // Kalman filter variables for rotation
        private Vector4 rotationEstimate; // Use Quaternion as Vector4
        private MatrixF rotationCovariance;
        public float rotationProcessNoise = 0.02f; //tune
        public float rotationMeasurementNoise = 0.1f; //tune

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogError("Rigidbody component not found on this GameObject!");
                enabled = false;
                return;
            }
            targetPosition = rb.position;
            targetRotation = rb.rotation;
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;

            // Initialize Kalman filter for position
            positionEstimate = rb.position;
            positionCovariance = MatrixF.MatrixMultiply(1f, MatrixF.Identity(3)); //initial uncertainty, tune

            // Initialize Kalman filter for rotation
            rotationEstimate = new Vector4(rb.rotation.x, rb.rotation.y, rb.rotation.z, rb.rotation.w);
            rotationCovariance = MatrixF.MatrixMultiply(1, MatrixF.Identity(4)); // Initial uncertainty, tune
        }

        void FixedUpdate()
        {
            if (useFixedUpdate)
            {
                interpolate(true);
            }
        }

        void Update()
        {
            if (!useFixedUpdate)
            {
                interpolate(false);
            }
        }

        private void interpolate(bool fixedTime)
        {
            if (isInterpolatingPosition)
            {
                InterpolatePosition(fixedTime);
            }
            if (isInterpolatingRotation)
            {
                InterpolateRotation(fixedTime);
            }
        }

        private void InterpolatePosition(bool fixedTime)
        {
            if (rb == null) return;

            float deltaTime = fixedTime ? Time.fixedDeltaTime : Time.deltaTime;
            Vector3 targetPositionWithOffset = targetPosition;
            if (joint != null)
            {
                targetPositionWithOffset = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
            }

            // 1. Prediction Step (Position)
            Vector3 predictedPosition = positionEstimate + velocity * deltaTime; //匀速运动模型
            positionCovariance = MatrixF.MatrixAdd(positionCovariance, MatrixF.MatrixMultiply(positionProcessNoise * deltaTime, MatrixF.Identity(3))); //增加过程噪声

            // Spring force
            Vector3 displacement = targetPositionWithOffset - predictedPosition;  // Use predicted position
            Vector3 force = displacement * positionStiffness;

            // Damping force
            Vector3 damping = velocity * positionDamping;
            Vector3 netForce = force - damping;

            // Update velocity and position
            velocity += netForce * deltaTime;
            Vector3 nextPosition = predictedPosition + velocity * deltaTime; //use predicted

            // 2. Measurement Update (Position)
            Vector3 measurement = nextPosition; //当前位置作为量测
            Vector3 innovation = measurement - predictedPosition;
            MatrixF innovationCovariance = MatrixF.MatrixAdd(positionCovariance,  MatrixF.MatrixMultiply(positionMeasurementNoise, MatrixF.Identity(3)));
            MatrixF kalmanGain = MatrixF.MatrixMultiply(positionCovariance, MatrixF.MatrixInverse(innovationCovariance));

            positionEstimate = predictedPosition + MatrixF.MatrixMultiply(kalmanGain, innovation);
            positionCovariance = MatrixF.MatrixMultiply(MatrixF.MatrixSubtract(MatrixF.Identity(3), kalmanGain), positionCovariance);

            rb.position = positionEstimate; // Apply the filtered position

            if (Vector3.Distance(rb.position, targetPositionWithOffset) < 0.01f)
            {
                rb.position = targetPositionWithOffset;
                isInterpolatingPosition = false;
                velocity = Vector3.zero; // Optionally reset velocity
            }
        }

        private void InterpolateRotation(bool fixedTime)
        {
            if (rb == null) return;

            float deltaTime = fixedTime ? Time.fixedDeltaTime : Time.deltaTime;
            Quaternion targetRotationWithOffset = targetRotation;
            if (joint != null)
            {
                targetRotationWithOffset = joint.connectedBody.transform.rotation;
            }

            // Convert Quaternions to Vector4 for Kalman filter
            Vector4 currentRotationV4 = new Vector4(rb.rotation.x, rb.rotation.y, rb.rotation.z, rb.rotation.w);
            Vector4 targetRotationV4 = new Vector4(targetRotationWithOffset.x, targetRotationWithOffset.y, targetRotationWithOffset.z, targetRotationWithOffset.w);


            // 1. Prediction Step (Rotation)
            Vector4 predictedRotationV4 = QuaternionToVector4(rb.rotation); // Use rb.rotation
            predictedRotationV4 = rotationEstimate + MatrixF.MatrixMultiply(deltaTime, new Vector4(angularVelocity.x, angularVelocity.y, angularVelocity.z, 0));
            rotationCovariance = MatrixF.MatrixAdd(rotationCovariance, MatrixF.MatrixMultiply(rotationProcessNoise * deltaTime, MatrixF.Identity(4)));

            // Spring torque
            Quaternion rotationDifference = targetRotationWithOffset * Quaternion.Inverse(rb.rotation);
            rotationDifference.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }
            Vector3 torque = axis * angle * rotationStiffness;

            // Damping torque
            Vector3 damping = angularVelocity * rotationDamping;
            Vector3 netTorque = torque - damping;

            // Update angular velocity and rotation
            angularVelocity += netTorque * deltaTime;
            Quaternion deltaRotation = Quaternion.AngleAxis(angularVelocity.magnitude * deltaTime, angularVelocity.normalized);
            Quaternion nextRotation = rb.rotation * deltaRotation;

            // 2. Measurement Update (Rotation)
            Vector4 measurement = QuaternionToVector4(nextRotation);
            Vector4 innovation = measurement - predictedRotationV4;
            MatrixF innovationCovariance = MatrixF.MatrixAdd(rotationCovariance, MatrixF.MatrixMultiply(rotationMeasurementNoise, MatrixF.Identity(4)));
            MatrixF kalmanGain = MatrixF.MatrixMultiply(rotationCovariance, innovationCovariance.MatrixInverse());

            rotationEstimate = predictedRotationV4 + MatrixF.MatrixMultiply(kalmanGain, innovation);
            rotationCovariance = MatrixF.MatrixMultiply(MatrixF.MatrixSubtract(MatrixF.Identity(4), kalmanGain), rotationCovariance);

            // Apply the filtered rotation
            rb.rotation = Vector4ToQuaternion(rotationEstimate);


            if (Quaternion.Angle(rb.rotation, targetRotationWithOffset) < 0.1f)
            {
                rb.rotation = targetRotationWithOffset;
                isInterpolatingRotation = false;
                angularVelocity = Vector3.zero; // Optionally reset angular velocity
            }
        }

        public MoSimResult<MoSimLib.Void> MoveToPosition(Vector3 newPosition)
        {
            var version = MoSim.ActiveVersion;
            MoveToPositionV1(newPosition);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void MoveToPositionV1(Vector3 newPosition)
        {
            targetPosition = newPosition;
            isInterpolatingPosition = true;
        }

        public MoSimResult<MoSimLib.Void> RotateToRotation(Quaternion newRotation)
        {
            var version = MoSim.ActiveVersion;
            RotateToRotationV1(newRotation);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void RotateToRotationV1(Quaternion newRotation)
        {
            targetRotation = newRotation;
            isInterpolatingRotation = true;
        }

        public MoSimResult<MoSimLib.Void> MoveToPositionAndRotation(Vector3 newPosition, Quaternion newRotation)
        {
            var version = MoSim.ActiveVersion;
            MoveToPositionAndRotationV1(newPosition, newRotation);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void MoveToPositionAndRotationV1(Vector3 newPosition, Quaternion newRotation)
        {
            targetPosition = newPosition;
            targetRotation = newRotation;
            isInterpolatingPosition = true;
            isInterpolatingRotation = true;
        }

        public MoSimResult<bool> IsInterpolating()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<bool>.Success(IsInterpolatingV1());
        }

        private bool IsInterpolatingV1()
        {
            return isInterpolatingPosition || isInterpolatingRotation;
        }

        public MoSimResult<MoSimLib.Void> StopInterpolation()
        {
            var version = MoSim.ActiveVersion;
            StopInterpolationV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void StopInterpolationV1()
        {
            isInterpolatingPosition = false;
            isInterpolatingRotation = false;
        }

        // Helper functions for Kalman filter with Quaternions
        private Vector4 QuaternionToVector4(Quaternion q)
        {
            return new Vector4(q.x, q.y, q.z, q.w);
        }

        private Quaternion Vector4ToQuaternion(Vector4 v)
        {
            return new Quaternion(v.x, v.y, v.z, v.w);
        }


        // MatrixF class for Kalman filter calculations
        public class MatrixF
        {
            public float[,] data;
            public int rows, cols;

            public MatrixF(int rows, int cols)
            {
                this.rows = rows;
                this.cols = cols;
                data = new float[rows, cols];
            }

            public MatrixF(float[,] initialData)
            {
                rows = initialData.GetLength(0);
                cols = initialData.GetLength(1);
                data = (float[,])initialData.Clone();
            }

            public static MatrixF Identity(int size)
            {
                MatrixF matrix = new MatrixF(size, size);
                for (int i = 0; i < size; i++)
                {
                    matrix.data[i, i] = 1f;
                }
                return matrix;
            }

            public static MatrixF MatrixMultiply(MatrixF a, MatrixF b)
            {
                if (a.cols != b.rows)
                {
                    throw new Exception("Matrices are not compatible for multiplication.");
                }

                MatrixF result = new MatrixF(a.rows, b.cols);
                for (int i = 0; i < a.rows; i++)
                {
                    for (int j = 0; j < b.cols; j++)
                    {
                        float sum = 0f;
                        for (int k = 0; k < a.cols; k++)
                        {
                            sum += a.data[i, k] * b.data[k, j];
                        }
                        result.data[i, j] = sum;
                    }
                }
                return result;
            }

            public static Vector3 MatrixMultiply(MatrixF a, Vector3 b)
            {
                if (a.cols != 3)
                {
                    throw new Exception("Matrix and Vector3 are not compatible for multiplication.");
                }

                Vector3 result = Vector3.zero;
                float[] vectorData = new float[] { b.x, b.y, b.z };

                for (int i = 0; i < a.rows; i++)
                {
                    float sum = 0f;
                    for (int j = 0; j < a.cols; j++)
                    {
                        sum += a.data[i, j] * vectorData[j];
                    }
                    if(i == 0)
                        result.x = sum;
                    else if(i == 1)
                        result.y = sum;
                    else
                        result.z = sum;
                }
                return result;
            }

            public static Vector4 MatrixMultiply(MatrixF a, Vector4 b)
            {
                if (a.cols != 4)
                {
                    throw new Exception("Matrix and Vector4 are not compatible for multiplication.");
                }

                Vector4 result = Vector4.zero;
                float[] vectorData = new float[] { b.x, b.y, b.z, b.w };

                for (int i = 0; i < a.rows; i++)
                {
                    float sum = 0f;
                    for (int j = 0; j < a.cols; j++)
                    {
                        sum += a.data[i, j] * vectorData[j];
                    }
                    if (i == 0)
                        result.x = sum;
                    else if (i == 1)
                        result.y = sum;
                    else if (i == 2)
                        result.z = sum;
                    else if (i == 3)
                        result.w = sum;
                }
                return result;
            }

            public static MatrixF MatrixMultiply(float scalar, MatrixF matrix)
            {
                MatrixF result = new MatrixF(matrix.rows, matrix.cols);
                for (int i = 0; i < matrix.rows; i++)
                {
                    for (int j = 0; j < matrix.cols; j++)
                    {
                        result.data[i, j] = scalar * matrix.data[i, j];
                    }
                }
                return result;
            }

            public static Vector3 MatrixMultiply(float scalar, Vector3 vector)
            {
                return new Vector3(scalar * vector.x, scalar * vector.y, scalar * vector.z);
            }

            public static Vector4 MatrixMultiply(float scalar, Vector4 vector)
            {
                return new Vector4(scalar * vector.x, scalar * vector.y, scalar * vector.z, scalar * vector.w);
            }

            public static MatrixF MatrixAdd(MatrixF a, MatrixF b)
            {
                if (a.rows != b.rows || a.cols != b.cols)
                {
                    throw new Exception("Matrices are not compatible for addition.");
                }

                MatrixF result = new MatrixF(a.rows, a.cols);
                for (int i = 0; i < a.rows; i++)
                {
                    for (int j = 0; j < a.cols; j++)
                    {
                        result.data[i, j] = a.data[i, j] + b.data[i, j];
                    }
                }
                return result;
            }

            public static MatrixF MatrixSubtract(MatrixF a, MatrixF b)
            {
                if (a.rows != b.rows || a.cols != b.cols)
                {
                    throw new Exception("Matrices are not compatible for subtraction.");
                }

                MatrixF result = new MatrixF(a.rows, a.cols);
                for (int i = 0; i < a.rows; i++)
                {
                    for (int j = 0; j < a.cols; j++)
                    {
                        result.data[i, j] = a.data[i, j] - b.data[i, j];
                    }
                }
                return result;
            }

            public MatrixF MatrixInverse()
            {
                return MatrixInverse(this);
            }

            public static MatrixF MatrixInverse(MatrixF matrix)
            {
                if (matrix.rows != matrix.cols)
                {
                    throw new Exception("Matrix is not square and cannot be inverted.");
                }

                int n = matrix.rows;
                MatrixF m = new MatrixF(matrix.data); // Create a copy to avoid modifying the original
                MatrixF identity = MatrixF.Identity(n);

                for (int p = 0; p < n; p++)
                {
                    float pivot = m.data[p, p];

                    if (Mathf.Abs(pivot) < 1e-6)
                    {
                        throw new Exception("Matrix is singular and cannot be inverted.");
                    }

                    for (int j = 0; j < n; j++)
                    {
                        m.data[p, j] /= pivot;
                        identity.data[p, j] /= pivot;
                    }

                    for (int i = 0; i < n; i++)
                    {
                        if (i != p)
                        {
                            float factor = m.data[i, p];
                            for (int j = 0; j < n; j++)
                            {
                                m.data[i, j] -= factor * m.data[p, j];
                                identity.data[i, j] -= factor * identity.data[p, j];
                            }
                        }
                    }
                }
                return identity;
            }
        }
    }
}

