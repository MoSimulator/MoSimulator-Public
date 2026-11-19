using System;
using System.Collections.Generic;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Robots;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using RobotFramework;
using RobotFramework.Controllers.GamePieceSystem;
using UnityEngine;

namespace Games.Reefscape.Scoring.Scorers
{
    public class ZoneFoulDetector : MonoBehaviour, IScorer
    {
        private const int MajorFoulPoints = 6;
        private const float FoulCooldownSeconds = 3f;

        private const float LaunchWatchSeconds = 4f;

        private static readonly Dictionary<string, Collider> ZoneCache = new();

        private readonly Dictionary<RobotBase, float> _lastFoulTime = new();

        private ReefscapeRobotReference _robot;
        private ReefscapeRobotGamePieceController _pieces;
        private ReefscapeRobotGamePieceController.GamePieceControllerNode _coral;

        private GamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData> _heldCoral;
        private GamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData> _released;
        private bool _releasedInZone;
        private float _watchStart;
        private float _lastLaunchFoulTime = float.NegativeInfinity;
        private MoSimLib.SecureFloat _foulPoints;

        public Alliance Alliance => _robot.Alliance;

        public void ResetDetectorState()
        {
            _lastFoulTime.Clear();
            _lastLaunchFoulTime = float.NegativeInfinity;
            _heldCoral = null;
            _released = null;
            _coral = null;

            _foulPoints = 0;
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is ReefscapeScoreData reefscapeData)
            {
                reefscapeData.PenaltyEarned += _foulPoints;
            }
        }

        private void Awake()
        {
            _robot = ReefscapeRobotReference.Find(gameObject);
            if (!_robot.IsValid)
            {
                throw new MissingComponentException($"{name}: needs a ReefscapeRobotBase or ReefscapeCommandRobotBase.");
            }

            _pieces = GetComponent<ReefscapeRobotGamePieceController>();
        }

        // G412
        private void FixedUpdate()
        {
            if (_pieces == null || MatchHasEnded)
            {
                return;
            }

            _coral ??= _pieces.GetPieceByName("Coral");
            if (_coral == null)
            {
                return;
            }

            if (_coral.HasPiece())
            {
                _heldCoral = _coral.controller;
                return;
            }

            if (_heldCoral != null)
            {
                _released = _heldCoral;
                _releasedInZone = InOwnReefZone();
                _watchStart = Time.time;
                _heldCoral = null;
            }

            WatchRelease();
        }

        private void WatchRelease()
        {
            if (_released == null)
            {
                return;
            }

            if (Time.time > _watchStart + LaunchWatchSeconds)
            {
                _released = null;
                return;
            }

            
            if (!_released.IsScored)
            {
                return;
            }

            var launchedOutsideZone = !_releasedInZone;
            _released = null;

            if (launchedOutsideZone)
            {
                AwardLaunchFoul();
            }
        }

        private void AwardLaunchFoul()
        {
            if (Time.time - _lastLaunchFoulTime < FoulCooldownSeconds)
            {
                return;
            }

            _foulPoints += MajorFoulPoints;
            _lastLaunchFoulTime = Time.time;
            PenaltyFlasher.Flash(_robot.Alliance);
        }

        private bool InOwnReefZone()
        {
            var bumpers = _robot.BumperCollider;
            if (bumpers == null)
            {
                throw new MissingComponentException($"{_robot.Robot.name} has no bumper collider to test against its zones.");
            }

            return Overlaps(bumpers, ByTag(_robot.Alliance == Alliance.Blue ? "BlueReefZone" : "RedReefZone"));
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryAwardFoul(collision.collider);
        }
        
        private void OnCollisionStay(Collision collision)
        {
            TryAwardFoul(collision.collider);
        }
        private static bool MatchHasEnded =>
            BaseGameManager.Instance != null && BaseGameManager.Instance.GameState == GameState.End;

        private void TryAwardFoul(Collider other)
        {
            if (MatchHasEnded)
            {
                return;
            }

            var opponent = FindOpponent(other);
            if (!opponent.IsValid || IsOnCooldown(opponent.Robot))
            {
                return;
            }

            if (!InOwnZone(opponent))
            {
                return;
            }

            _foulPoints += MajorFoulPoints;
            _lastFoulTime[opponent.Robot] = Time.time;
            PenaltyFlasher.Flash(_robot.Alliance);
        }

        private ReefscapeRobotReference FindOpponent(Collider other)
        {
            if (other == null)
            {
                return default;
            }

            var struck = ReefscapeRobotReference.Find(other.gameObject);
            return struck.IsValid && struck.Alliance != _robot.Alliance ? struck : default;
        }

        private bool IsOnCooldown(RobotBase opponent)
        {
            return _lastFoulTime.TryGetValue(opponent, out var last) && Time.time - last < FoulCooldownSeconds;
        }

        private static bool InOwnZone(ReefscapeRobotReference robot)
        {
            var bumpers = robot.BumperCollider;
            if (bumpers == null)
            {
                throw new MissingComponentException($"{robot.Robot.name} has no bumper collider to test against its zones.");
            }

            var blue = robot.Alliance == Alliance.Blue;

            return Overlaps(bumpers, ByTag(blue ? "BlueReefZone" : "RedReefZone"))
                   || Overlaps(bumpers, ByTag(blue ? "BlueBargeZone" : "RedBargeZone"));
        }

        private static bool Overlaps(Collider bumpers, Collider zone)
        {
            var bumperTransform = bumpers.transform;
            var zoneTransform = zone.transform;

            return Physics.ComputePenetration(
                bumpers, bumperTransform.position, bumperTransform.rotation,
                zone, zoneTransform.position, zoneTransform.rotation,
                out _, out _);
        }

        private static Collider ByTag(string zoneTag)
        {
            if (ZoneCache.TryGetValue(zoneTag, out var cached) && cached != null)
            {
                return cached;
            }

            var found = GameObject.FindGameObjectWithTag(zoneTag);
            if (found == null)
            {
                throw new InvalidOperationException($"No GameObject tagged {zoneTag}; G427 cannot run.");
            }

            var zone = found.GetComponent<Collider>();
            if (zone == null)
            {
                throw new MissingComponentException($"{found.name} has no Collider for its penalty zone.");
            }

            ZoneCache[zoneTag] = zone;
            return zone;
        }
    }
}
