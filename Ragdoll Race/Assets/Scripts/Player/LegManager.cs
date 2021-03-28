﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DitzelGames.FastIK;

public class LegManager : MonoBehaviour
{

    [Header("References")]
    [SerializeField] private ActiveRagdoll activeRagdoll;
    [SerializeField] private Rigidbody pelvisRigidbody;
    [Space]
    [SerializeField] private FastIKFabric leftLegIK;
    [SerializeField] private FastIKFabric rightLegIK;
    [Space]
    [SerializeField] private ConfigurableJoint leftUpperJoint;
    [SerializeField] private ConfigurableJoint rightUpperJoint;
    [SerializeField] private ConfigurableJoint leftLowerJoint;
    [SerializeField] private ConfigurableJoint rightLowerJoint;


    [Header("Movement Properties")]
    [SerializeField] private float movingStepOffset;
    [SerializeField] private float minDisplacementToMove;


    [Header("Step Animation Properties")]
    [SerializeField] private float stepCycleLength;
    [SerializeField] private float stepAnimationDuration;
    [SerializeField] private float stepAnimationHeight;
    [SerializeField] private AnimationCurve stepDistanceCurve;
    [SerializeField] private AnimationCurve stepHeightCurve;


    [Header("Step Calculation Properties")]
    [SerializeField] private float standingFeetWidth;
    [SerializeField] private float minLegLength;
    [SerializeField] private float maxLegLength;
    [SerializeField] private LayerMask walkableLayers;
    

    
    // Private Variables

    private bool useDynamicGait;
    private float timeSinceLastStep;
    private bool leftLegMoving;

    private Quaternion leftUpperRotation;
    private Quaternion rightUpperRotation;
    private Quaternion leftLowerRotation;
    private Quaternion rightLowerRotation;



    // Unity Functions
    
    void Start()
    {
        leftLowerRotation = leftLowerJoint.transform.localRotation;
        leftUpperRotation = leftUpperJoint.transform.localRotation;
        rightLowerRotation = rightUpperJoint.transform.localRotation;
        rightUpperRotation = rightUpperJoint.transform.localRotation;
    }


    void FixedUpdate()
    {
        timeSinceLastStep += Time.fixedDeltaTime;

        // Move the current leg IK bones after its alotted time
        if(timeSinceLastStep >= stepCycleLength / 2){
            timeSinceLastStep = 0;

            FastIKFabric currentLeg = leftLegMoving ? leftLegIK : rightLegIK;
            Transform currentTarget = currentLeg.Target;

            Vector3 desiredPosition = CastToGround(leftLegMoving, out Vector3 groundNormal);
            float displacementFromDefault = Vector3.Distance(currentTarget.position, desiredPosition);

            if(displacementFromDefault >= minDisplacementToMove){
                StartCoroutine(MoveLeg(currentLeg, desiredPosition, groundNormal));
            }

            leftLegMoving = !leftLegMoving;
        }

        
        // Update position and rotation targets for the physical legs
        // save for later:  poseTarget = Quaternion.Inverse(leftLowerJoint.transform.parent.rotation) * GetLegJointTargetRotation(true, true);
        Quaternion localRotation;

        localRotation = Quaternion.Inverse(leftLowerJoint.transform.parent.rotation) * GetLegJointTargetRotation(true, true);
        leftLowerJoint.SetTargetRotationLocal(localRotation, leftLowerRotation);

        localRotation = Quaternion.Inverse(leftUpperJoint.transform.parent.rotation) * GetLegJointTargetRotation(true, false);
        leftUpperJoint.SetTargetRotationLocal(localRotation, leftUpperRotation);

        localRotation = Quaternion.Inverse(rightLowerJoint.transform.parent.rotation) * GetLegJointTargetRotation(false, true);
        rightLowerJoint.SetTargetRotationLocal(localRotation, rightLowerRotation);

        localRotation = Quaternion.Inverse(rightUpperJoint.transform.parent.rotation) * GetLegJointTargetRotation(false, false);
        rightUpperJoint.SetTargetRotationLocal(localRotation, rightUpperRotation);

        //leftLowerJoint.targetRotation = GetLegJointTargetRotation(true, true) * Quaternion.Inverse(leftLowerJoint.transform.parent.rotation);
        //leftUpperJoint.targetRotation = GetLegJointTargetRotation(true, false) * Quaternion.Inverse(leftUpperJoint.transform.parent.rotation);
        //rightLowerJoint.targetRotation = GetLegJointTargetRotation(false, true) * Quaternion.Inverse(rightLowerJoint.transform.parent.rotation);
        //rightUpperJoint.targetRotation = GetLegJointTargetRotation(false, false) * Quaternion.Inverse(rightUpperJoint.transform.parent.rotation);

        /*leftLowerJoint.targetRotation = GetLegJointTargetRotation(true, true) * Quaternion.Inverse(leftLowerJoint.connectedBody.transform.rotation);
        leftUpperJoint.targetRotation = GetLegJointTargetRotation(true, false) * Quaternion.Inverse(leftUpperJoint.connectedBody.transform.rotation) ;
        rightLowerJoint.targetRotation = GetLegJointTargetRotation(false, true) * Quaternion.Inverse(rightLowerJoint.connectedBody.transform.rotation);
        rightUpperJoint.targetRotation = GetLegJointTargetRotation(false, false) * Quaternion.Inverse(rightUpperJoint.connectedBody.transform.rotation);*/
        
        //leftLowerJoint.targetRotation = Quaternion.Inverse(leftLowerRotation) * GetLegJointTargetRotation(true, true);
        //leftUpperJoint.targetRotation = Quaternion.Inverse(leftUpperRotation) * GetLegJointTargetRotation(true, false);
        //rightLowerJoint.targetRotation = Quaternion.Inverse(rightLowerRotation) * GetLegJointTargetRotation(false, true);
        //rightUpperJoint.targetRotation = Quaternion.Inverse(rightUpperRotation) * GetLegJointTargetRotation(false, false);

        //leftLowerJoint.transform.rotation = GetLegJointTargetRotation(true, true);
        //leftUpperJoint.transform.rotation = GetLegJointTargetRotation(true, false);
        //rightLowerJoint.transform.rotation = GetLegJointTargetRotation(false, true);
        //rightUpperJoint.transform.rotation = GetLegJointTargetRotation(false, false);

    }



    // Private Functions

    private Vector3 CastToGround(bool isLeft, out Vector3 groundNormal){
        // Casts a ray down and through the desired position to find solid ground

        // Calculate the horizontal and max possible vertical position of the foot
        Vector3 rayOrigin = pelvisRigidbody.worldCenterOfMass;
        rayOrigin += (pelvisRigidbody.transform.right.ProjectHorizontal() * (standingFeetWidth/2) * (isLeft ? -1 : 1));
        rayOrigin += pelvisRigidbody.velocity.ProjectHorizontal() * movingStepOffset;
        rayOrigin += Vector3.down * minLegLength;


        // Cast a ray down from above the desired position to find solid ground
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHitInfo, maxLegLength - minLegLength, walkableLayers)){
            // If solid ground is detected, return the hit point
            groundNormal = groundHitInfo.normal;
            return groundHitInfo.point;
        }
        else{
            // If no ground is detected, fully extend leg
            groundNormal = Vector3.up;
            return rayOrigin + (Vector3.down * (maxLegLength - minLegLength));
        }     
    }


    private IEnumerator MoveLeg(FastIKFabric leg, Vector3 newPosition, Vector3 newNormal){
        // Moves the given leg along a path defined by direction to the new target and the step animation curve

        Vector3 oldPosition = leg.Target.position;
        Vector3 totalDisplacement = newPosition - oldPosition;

        newNormal.Normalize();

        float timeGradient = 0;
        while (timeGradient <= 1){
            timeGradient = Mathf.Clamp01(timeGradient);

            float spaceGradient = stepDistanceCurve.Evaluate(timeGradient);

            Vector3 currentHeight = stepHeightCurve.Evaluate(spaceGradient) * stepAnimationHeight * newNormal;
            Vector3 currentDirection = spaceGradient * totalDisplacement;

            Vector3 currentPosition = oldPosition + currentDirection + currentHeight;
            leg.Target.position = currentPosition;

            timeGradient += Time.fixedDeltaTime / stepAnimationDuration;

            yield return new WaitForFixedUpdate();
        }

        leg.Target.position = newPosition;

        yield break;
    }


    private FastIKFabric GetLeg(bool isLeft){
        return isLeft ? leftLegIK : rightLegIK;
    }


    private Quaternion GetLegJointTargetRotation(bool isLeft, bool isLowerLeg){
        // Calculates the target position and rotation of a leg segment rigidbody using two IK bones
        
        FastIKFabric leg = GetLeg(isLeft);

        Vector3 outerBonePosition = isLowerLeg ? leg.transform.position : leg.transform.parent.position;
        Vector3 innerBonePosition = isLowerLeg ? leg.transform.parent.position : leg.transform.parent.parent.position;

        Vector3 jointPosition = (outerBonePosition + innerBonePosition) / 2;
        Vector3 jointDirectionHorizontal = leg.Pole.position.ProjectHorizontal() - jointPosition.ProjectHorizontal();

        Quaternion yawRotation = Quaternion.FromToRotation(Vector3.forward, jointDirectionHorizontal);
        Quaternion pitchRotation = Quaternion.FromToRotation(Vector3.up, innerBonePosition - outerBonePosition);

        return pitchRotation * yawRotation;
    }

}
