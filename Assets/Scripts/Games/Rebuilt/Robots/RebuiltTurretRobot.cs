using System.Collections;
using System.Collections.Generic;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

public class RebuiltTurretRobot : RebuiltRobotBase
{
    [Header("SWM")] 
    [SerializeField] protected Transform swmRef;
    [SerializeField] protected SOTMSettings swmSettings;
    private Vector3 _smoothedVelocity;

    protected Rigidbody rb;
    protected DriveController driveController;
    // Start is called before the first frame update
    protected override void Init()
    {
        rb = GetComponent<Rigidbody>();
        driveController = GetComponent<DriveController>();
        Scheduler.AddProtectedSystem(HandleSwm, StateHandler, ShooterHandler);
    }

    protected virtual void HandleSwm()
    {
        Vector3 robotVelocity = rb.velocity;
        _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, robotVelocity, swmSettings.velocityDampeningValue);
        swmRef.position = swmRef.parent.position + (_smoothedVelocity * swmSettings.timeOfFlight);
    }

    protected virtual void ShooterHandler()
    {
        if (OuttakeAction)
        {
            if (IntakeAction)
            {
                IntakingAndShooting();
            }
            else
            {
                Shooting();
            }

            // Apply feed commands last so they override the non-feeding speeds
            // set by the shooting callbacks, only once the shot is ready.
            if (Aligned()) Feed();
        }
        else if (IntakeAction)
        {
            Intaking();
        }
        else
        {
            Idling();
        }
    }

    private void StateHandler()
    {
        if (InAllianceZone())
        {
            AllianceZone();
        }
        else
        {
            NeutralZone();
        }
    }

    protected virtual void AllianceZone()
    {
        
    }

    protected virtual void NeutralZone()
    {
        
    }
    
    protected virtual void Intaking()
    {
        
    }
    
    protected virtual void IntakingAndShooting()
    {
        
    }
    
    /// <summary>
    /// Called every tick while shooting is requested, regardless of alignment.
    /// Command shoot targets and flywheel speeds here, and reset feeders to their
    /// non-feeding speeds so losing alignment stops feeding on the next tick.
    /// IntakingAndShooting() must do the same when both inputs are held.
    /// </summary>
    protected virtual void Shooting()
    {
        
    }

    /// <summary>
    /// Called after the shooting callback, only when Aligned() is true. Set feed
    /// speeds here; these commands take precedence over the shooting callback.
    /// </summary>
    protected virtual void Feed()
    {
    }
    
    protected virtual void Idling()
    {
        
    }
    
    /// <summary>Return true when aiming and flywheel speed are ready to feed.</summary>
    protected virtual bool Aligned()
    {
        return false;
    }
}
