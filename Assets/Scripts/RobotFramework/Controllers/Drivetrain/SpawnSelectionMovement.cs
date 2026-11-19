using System;
using MoSimCore.Enums;
using MoSimLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RobotFramework.Controllers.Drivetrain
{
    public class SpawnSelectionMovement : MonoBehaviour
    {
        private float maxSpeed = 1.5f;
        private float rotationSpeed = 200f;

        [Header("Alliance")]
        [SerializeField] public Alliance alliance = Alliance.Blue;

        [HideInInspector] public MeshRenderer[] bumpers;
        [HideInInspector] public Material bumperMat;

        private Rigidbody _rigidBody;
        private Gamepad _gamepad;
        private SpawnpointSelectionSettings _settings;

        private Boolean _initialized;

        public MoSimResult<MoSimLib.Void> Init(Alliance a, MeshRenderer[] bump, Material mat)
        {
            var version = MoSim.ActiveVersion;

            InitV1(a, bump, mat);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void InitV1(Alliance a, MeshRenderer[] bump, Material mat)
        {
            alliance = a;

            bumpers = bump;
            bumperMat = mat;

            _initialized = true;

            _rigidBody = GetComponent<Rigidbody>();
            _rigidBody.maxLinearVelocity = maxSpeed;
            LevelAndFreezeRotation();
        }

        private void Start()
        {
            _settings = GetComponent<SpawnpointSelectionSettings>();

            if (!_initialized)
            {
                _rigidBody = GetComponent<Rigidbody>();
                _rigidBody.maxLinearVelocity = maxSpeed;
                LevelAndFreezeRotation();
            }
        }

        private void LevelAndFreezeRotation()
        {
            _rigidBody.constraints = RigidbodyConstraints.FreezeRotationX
                                    | RigidbodyConstraints.FreezeRotationZ;
        }

        void FixedUpdate()
        {
            _gamepad = Gamepad.current;
            HandleMovement();
            HandleRotation();
            _rigidBody.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            ColorBumpers(_settings);
            
            
            float yaw = _rigidBody.rotation.eulerAngles.y;
            _rigidBody.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        
        private void ColorBumpers(SpawnpointSelectionSettings settings)
        {
            if (bumpers != null && bumperMat != null && settings != null)
            {
                foreach (var mesh in bumpers)
                {
                    Material[] mats = mesh.materials;
                    mats[settings.materialIndex] = bumperMat; 
                    mesh.materials = mats;
                }
            }
        }

        private Vector2 ReadMoveInput()
        {
            Vector2 kb = Vector2.zero;
            if (Keyboard.current != null)
            {
                kb.x = ((Keyboard.current.wKey.isPressed ? 1 : 0) - (Keyboard.current.sKey.isPressed ? 1 : 0)) * (alliance == Alliance.Blue ? 1 : -1);
                kb.y = ((Keyboard.current.aKey.isPressed ? 1 : 0) - (Keyboard.current.dKey.isPressed ? 1 : 0)) * (alliance == Alliance.Blue ? 1 : -1);
            }

            Vector2 gamepad = Vector2.zero;
            if (_gamepad != null)
            {
                gamepad.y = -_gamepad.leftStick.ReadValue().x * (alliance == Alliance.Blue ? 1 : -1);
                gamepad.x = _gamepad.leftStick.ReadValue().y * (alliance == Alliance.Blue ? 1 : -1);
            }
            Vector2 raw = gamepad.sqrMagnitude > 0.1f ? gamepad : kb;
            
            return Vector2.ClampMagnitude(raw, 1f);
        }

        private float ReadRotationInput()
        {
            float kb = 0f;
            if (Keyboard.current != null)
            {
                kb = (Keyboard.current.jKey.isPressed ? 1 : 0) - (Keyboard.current.lKey.isPressed ? 1 : 0);
            }

            float gamepad = _gamepad != null ? -_gamepad.rightStick.ReadValue().x : 0f;

            return Mathf.Abs(gamepad) > 0.1f ? gamepad : kb;
        }

        private void HandleMovement()
        {
            Vector2 input = ReadMoveInput();

            if (input.sqrMagnitude < 0.0001f)
            {
                _rigidBody.constraints |= RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
                return;
            }

            _rigidBody.constraints &= ~(RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ);

            Vector3 delta = new Vector3(input.x, 0f, input.y) * maxSpeed * Time.fixedDeltaTime;
            _rigidBody.MovePosition(_rigidBody.position + delta);
        }

        private void HandleRotation()
        {
            float input = ReadRotationInput();

            if (Mathf.Abs(input) < 0.0001f)
            {
                _rigidBody.angularVelocity = Vector3.zero;
                _rigidBody.constraints |= RigidbodyConstraints.FreezeRotationY;
                return;
            }

            _rigidBody.constraints &= ~RigidbodyConstraints.FreezeRotationY;

            float deltaDeg = -input * rotationSpeed * Time.fixedDeltaTime;
            _rigidBody.MoveRotation(_rigidBody.rotation * Quaternion.Euler(0f, deltaDeg, 0f));
        }
    }
}
