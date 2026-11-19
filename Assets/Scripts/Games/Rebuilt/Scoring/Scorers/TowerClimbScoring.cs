using System.Collections.Generic;
using System.Linq;
using Games.Rebuilt.Robots;
using Games.Rebuilt.Scoring;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

public class TowerClimbScoring : MonoBehaviour, IScorer
{
    [field: SerializeField] public Alliance Alliance { get; private set; }

    [SerializeField] private BoxCollider validClimbCheck;
    [SerializeField] private BoxCollider l2BumperZone;
    [SerializeField] private BoxCollider l3BumperZone;
    [SerializeField] private BoxCollider l1BumperZone;

    private OverlapBoxBounds ValidClimbCheck;
    private OverlapBoxBounds L2BumperZone;
    private OverlapBoxBounds L3BumperZone;
    private OverlapBoxBounds L1BumperZone;

    private HashSet<Rigidbody> rawRigidbodies;
    private HashSet<RebuiltRobotBase> robots;

    private HashSet<Collider> allL2Colliders;
    private HashSet<Collider> allL3Colliders;
    private HashSet<Collider> allL1Colliders;

    private Dictionary<RebuiltRobotBase, ClimbHeight> robotsClimbed;

    private Dictionary<RebuiltRobotBase, DriveController> quickLookup;

    private SecureFloat scoreAdded = 0;

    private SecureFloat finalAutoAdded = 0;

    // For TowerProtection.cs L3 climb
    private readonly HashSet<RebuiltRobotBase> awardedLevel3 = new();

    public void AwardLevel3(RebuiltRobotBase robot)
    {
        if (robot != null)
        {
            awardedLevel3.Add(robot);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        validClimbCheck.isTrigger = true;
        l2BumperZone.isTrigger = true;
        l3BumperZone.isTrigger = true;
        l1BumperZone.isTrigger = true;
        ValidClimbCheck = new OverlapBoxBounds(validClimbCheck, false);
        L2BumperZone = new OverlapBoxBounds(l2BumperZone, false);
        L3BumperZone = new OverlapBoxBounds(l3BumperZone, false);
        L1BumperZone = new OverlapBoxBounds(l1BumperZone, false);

        rawRigidbodies = new HashSet<Rigidbody>();
        robots = new HashSet<RebuiltRobotBase>();
        allL2Colliders = new HashSet<Collider>();
        allL3Colliders = new HashSet<Collider>();
        allL1Colliders = new HashSet<Collider>();

        quickLookup = new Dictionary<RebuiltRobotBase, DriveController>();
        robotsClimbed = new Dictionary<RebuiltRobotBase, ClimbHeight>();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        rawRigidbodies.Clear();
        var climbCheck = ValidClimbCheck.OverlapBox(LayerMask.GetMask("Robot"));
        foreach (var lowCollider in climbCheck)
        {
            rawRigidbodies.Add(lowCollider.attachedRigidbody);
        }

        allL2Colliders.Clear();
        var l2ClimbCheck = L2BumperZone.OverlapBox(LayerMask.GetMask("Robot"));
        foreach (var highCollider in l2ClimbCheck)
        {
            allL2Colliders.Add(highCollider);
        }

        allL3Colliders.Clear();
        var l3ClimbCheck = L3BumperZone.OverlapBox(LayerMask.GetMask("Robot"));
        foreach (var highCollider in l3ClimbCheck)
        {
            allL3Colliders.Add(highCollider);
        }

        allL1Colliders.Clear();
        var l1ClimbCheck = L1BumperZone.OverlapBox(LayerMask.GetMask("Robot"));
        foreach (var highCollider in l1ClimbCheck)
        {
            allL1Colliders.Add(highCollider);
        }

        foreach (var root in rawRigidbodies
            .Select(possibleRobot => Utils.FindParentObjectComponent<RebuiltRobotBase>(possibleRobot.gameObject))
            .Where(root => root && root.Alliance == Alliance))
        {
            robots.Add(root);
        }

        foreach (var robot in robots.ToList())
        {
            if (!quickLookup.TryGetValue(robot, out var controller))
            {
                controller = robot.GetComponent<DriveController>();
                quickLookup[robot] = controller;
            }

            if (controller && controller.IsTouchingGround)
            {
                robots.Remove(robot);
            }
        }

        robotsClimbed.Clear();
        foreach (var robot in robots)
        {
            var bump = robot.BumperCollider;

            if (allL1Colliders.Contains(bump))
            {
                robotsClimbed[robot] = ClimbHeight.L1;
            }
            if (allL2Colliders.Contains(bump))
            {
                robotsClimbed[robot] = ClimbHeight.L2;
            }
            else if (allL3Colliders.Contains(bump))
            {
                robotsClimbed[robot] = ClimbHeight.L3;
            }
            else
            {
                robotsClimbed[robot] = ClimbHeight.L1;
            }
        }


    }



    void IScorer.AddScore(IScoreData scoreData, GameState gameState)
    {
        if (scoreData is RebuiltScoreData rebuiltScoreData)
        {
            if (gameState == GameState.Endgame)
            {
                SecureFloat totalScore = 0;
                foreach (var robot in robotsClimbed)
                {
                    var scoreValue = 0;
                    if (awardedLevel3.Contains(robot.Key))
                    {
                        // G420 awards level 3
                        scoreValue = 30;
                    }
                    else if (robot.Value == ClimbHeight.L1)
                    {
                        scoreValue = 10;
                    }
                    else if (robot.Value == ClimbHeight.L2)
                    {
                        scoreValue = 20;
                    }
                    else if (robot.Value == ClimbHeight.L3)
                    {
                        scoreValue = 30;
                    }

                    totalScore += HubScoring.godMode ? scoreValue * 100 : scoreValue;
                }

                // Still keep the penalty if knocked off the tower
                foreach (var awarded in awardedLevel3)
                {
                    if (awarded != null && !robotsClimbed.ContainsKey(awarded))
                    {
                        totalScore += HubScoring.godMode ? 30 * 100 : 30;
                    }
                }

                scoreAdded = totalScore;
            }
            else if (gameState == GameState.Auto)
            {
                SecureFloat totalScore = 0;
                foreach (var robot in robotsClimbed)
                {
                    var scoreValue = 0;
                    if (robot.Value == ClimbHeight.L1)
                    {
                        scoreValue = 15;
                    }
                    else if (robot.Value == ClimbHeight.L2)
                    {
                        scoreValue = 15;
                    }
                    else if (robot.Value == ClimbHeight.L3)
                    {
                        scoreValue = 15;
                    }

                    totalScore += HubScoring.godMode ? scoreValue * 100 : scoreValue;
                }

                finalAutoAdded = totalScore;
            }

            rebuiltScoreData.ClimbAuto = finalAutoAdded;
            rebuiltScoreData.ClimbTele += scoreAdded;
        }
    }
}

public enum ClimbHeight
{
    L1,
    L2,
    L3,
}
