using System.Collections.Generic;
using Games.Rebuilt.Robots;
using Games.Rebuilt.Scoring;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

// G420
public class TowerProtection : MonoBehaviour, IScorer
{
    private const int MajorFoulPoints = 15;
    private const float FoulCooldownSeconds = 3f;

    public static int RedInstances;
    public static int BlueInstances;

    [field: SerializeField] public Alliance Alliance { get; private set; }

    [Tooltip("Uprights and rungs a robot has to be touching to count as on the Tower")]
    [SerializeField] private Collider[] towerColliders;

    [Tooltip("Scorer for this Tower, so a protected robot can be awarded level 3")]
    [SerializeField] private TowerClimbScoring climbScoring;

    private readonly List<RebuiltRobotBase> allRobots = new();
    private readonly HashSet<RebuiltRobotBase> onTower = new();
    private readonly Dictionary<RebuiltRobotBase, float> lastFoulTime = new();
    private readonly Dictionary<RebuiltRobotBase, DriveController> driveControllers = new();

    private int robotMask;

    private void Awake()
    {
        RedInstances = 0;
        BlueInstances = 0;
    }

    private void Start()
    {
        robotMask = LayerMask.GetMask("Robot");

        if (towerColliders == null || towerColliders.Length == 0)
        {
            Debug.LogError("TowerProtection: no tower colliders assigned on " + gameObject.name);
            enabled = false;
        }
    }

    void IScorer.AddScore(IScoreData scoreData, GameState gameState)
    {
        if (scoreData is not RebuiltScoreData rebuiltScoreData)
        {
            return;
        }

        var instances = Alliance == Alliance.Blue ? BlueInstances : RedInstances;
        rebuiltScoreData.PenaltyEarned += instances * MajorFoulPoints;
    }

    private void FixedUpdate()
    {
        if (BaseGameManager.Instance == null || BaseGameManager.Instance.GameState != GameState.Endgame)
        {
            return;
        }

        RefreshRobots();
        FindRobotsOnTower();

        if (onTower.Count == 0)
        {
            return;
        }

        foreach (var climber in onTower)
        {
            foreach (var opponent in allRobots)
            {
                if (opponent == null || opponent.Alliance == Alliance || opponent.BumperCollider == null)
                {
                    continue;
                }

                if (climber.BumperCollider == null || !Touching(climber.BumperCollider, opponent.BumperCollider))
                {
                    continue;
                }

                if (lastFoulTime.TryGetValue(opponent, out var last) && Time.time - last < FoulCooldownSeconds)
                {
                    continue;
                }

                lastFoulTime[opponent] = Time.time;

                if (opponent.Alliance == Alliance.Blue)
                {
                    BlueInstances++;
                }
                else
                {
                    RedInstances++;
                }

                PenaltyFlasher.Flash(opponent.Alliance);

                if (climbScoring != null && IsOffGround(climber))
                {
                    climbScoring.AwardLevel3(climber);
                }
            }
        }
    }

    private bool IsOffGround(RebuiltRobotBase robot)
    {
        if (!driveControllers.TryGetValue(robot, out var controller))
        {
            controller = robot.GetComponent<DriveController>();
            driveControllers[robot] = controller;
        }

        return controller != null && !controller.IsTouchingGround;
    }

    private void FindRobotsOnTower()
    {
        onTower.Clear();

        foreach (var towerCollider in towerColliders)
        {
            if (towerCollider == null)
            {
                continue;
            }

            var bounds = towerCollider.bounds;
            foreach (var hit in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, robotMask))
            {
                var robot = hit.GetComponentInParent<RebuiltRobotBase>();
                if (robot != null && robot.Alliance == Alliance)
                {
                    onTower.Add(robot);
                }
            }
        }
    }

    private void RefreshRobots()
    {
        foreach (var robot in allRobots)
        {
            if (robot == null)
            {
                allRobots.Clear();
                break;
            }
        }

        if (allRobots.Count > 0)
        {
            return;
        }

        allRobots.AddRange(FindObjectsByType<RebuiltRobotBase>(FindObjectsInactive.Exclude,
            FindObjectsSortMode.None));
    }

    private static bool Touching(Collider a, Collider b)
    {
        var aTransform = a.transform;
        var bTransform = b.transform;

        return Physics.ComputePenetration(
            a, aTransform.position, aTransform.rotation,
            b, bTransform.position, bTransform.rotation,
            out _, out _);
    }
}
