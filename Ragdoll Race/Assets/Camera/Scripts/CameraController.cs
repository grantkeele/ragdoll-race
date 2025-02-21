using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{

    [Header("Component References")]
    public PlayersManager playersManager;
    [HideInInspector] public Camera mainCamera;
    [HideInInspector] public Rigidbody rb;
    public Transform anchorTransform;
    private CameraShakeManager cameraShakeManager;
    

    [Header("Framing Parameters")]
    public float horizontalAngle;
    public float verticalAngle;
    [Space]
    public float horizontalPaddingDistance;
    public float verticalPaddingDistance;
    [Space]
    public float cameraFOV;


    [Header("Distance Constraints")]
    public float targetBoundsHorizontal;
    public float targetBoundsVertical;


    [Header("Spring Parameters")]
    public float springFrequency;
    public float springDamping;



    [Header("Control Flags")]
    public bool freezeInPlace = false;
    public bool ignoreDistanceConstraints = false;



    private List<Transform> additionalTargets = new List<Transform>();




    // Main Functions

    private void Awake()
    {
        mainCamera = GetComponentInChildren<Camera>();
        rb = GetComponent<Rigidbody>();
        cameraShakeManager = GetComponent<CameraShakeManager>();
    }

    void FixedUpdate(){

        List<Player> allPlayers = playersManager.GetAllPlayers();
        int numPlayers = allPlayers.Count;

        if(numPlayers > 0){
            // Get the world space positions of all players' feet and heads, to get full enclosing volume
            List<Vector3> playerFeetPositions = playersManager.GetPositions(allPlayers).AddVector(0, -playersManager.characterPelvisHeight, 0);
            List<Vector3> playerHeadPositions = playerFeetPositions.AddVector(0, playersManager.characterHeadHeight, 0);


            List<Vector3> playerTargetsWorld = new List<Vector3>();
            playerTargetsWorld.AddRange(playerFeetPositions);
            playerTargetsWorld.AddRange(playerHeadPositions);
            playerTargetsWorld.AddRange(additionalTargets.GetPositions());

            List<Vector3> playerTargetsLocal = transform.InverseTransformPoints(playerTargetsWorld);


            // Find the corners of a box surrounding the players in camera space, add padding space
            Vector3 maxDimensionsLocal = playerTargetsLocal.MaxComponents() + new Vector3(horizontalPaddingDistance, verticalPaddingDistance, 0);
            Vector3 minDimensionsLocal = playerTargetsLocal.MinComponents() - new Vector3(horizontalPaddingDistance, verticalPaddingDistance, 0);


            //
            if(!ignoreDistanceConstraints){
                // Clamp the box enclosing the players to inside the bounds
                ClampBoxDimensionsToBounds(ref maxDimensionsLocal, ref minDimensionsLocal);

                // Resize the player box to match the camera's aspect ratio
                ResizeFrameToAspectRatio(ref maxDimensionsLocal, ref minDimensionsLocal, mainCamera.aspect);
                
                
                // Shift the clamped, correct aspect ratio box to fit within the bounds
                ShiftBoxInsideBounds(ref maxDimensionsLocal, ref minDimensionsLocal);
            }
            


            // Find the local and world space center point of the target box
            Vector3 centerPointLocal = (maxDimensionsLocal + minDimensionsLocal) / 2;
            Vector3 centerPointWorld = transform.TransformPoint(centerPointLocal);
            

            // Calculate the enclosing volume and frame the players within the camera's view
            Vector3 enclosingDimensions = (maxDimensionsLocal - minDimensionsLocal);
            float cameraFramingDistance = CalculateFramingDistance(mainCamera, enclosingDimensions);
            Vector3 targetCameraPosition = centerPointWorld + (-transform.forward * cameraFramingDistance);


            // Calculate and apply spring forces on the camera
            Vector3 relativePosition = rb.position - targetCameraPosition;
            Vector3 relativeVelocity = rb.velocity - playersManager.AverageVelocity(allPlayers);
            
            Vector3 springAcceleration = DampedSpring.GetDampedSpringAcceleration(relativePosition, relativeVelocity, springFrequency, springDamping);
            rb.AddForce(springAcceleration, ForceMode.Acceleration);
        }
   
    }



    // Public Functions

    public bool AddAdditionalTarget(Transform target){
        // Adds target and returns true if the target is not already in the list
        if(!additionalTargets.Contains(target)){
            additionalTargets.Add(target);
            return true;
        }
        else{ return false; }
    }

    public bool RemoveAdditionalTarget(Transform target){
        // Removes target and returns true if the target existed
        if(additionalTargets.Contains(target)){
            additionalTargets.Remove(target);
            return true;
        }
        else{ return false; }
    }

    public void ClearAdditionalTargets(){
        additionalTargets.Clear();
    }


    public Vector3 GetCameraForwardDirection(){
        // Calculates the direction the camera is facing, projected onto the horizontal plane
        Vector3 camForwardDir = transform.forward.ProjectHorizontal().normalized;
        return camForwardDir;
    }


    public Transform CreateTemporaryAdditionalTarget(Vector3 position, float lifetime){
        // Creates a new transform at the specified position and registers it as an additional camera target.
        // After its lifetime has passed, remove from target list and destroy transform

        Transform target = new GameObject("temporary camera target").transform;
        target.position = position;

        AddAdditionalTarget(target);
        StartCoroutine(RemoveTargetAfterTime(target, lifetime, true));

        return target;
    }
    private IEnumerator RemoveTargetAfterTime(Transform target, float time, bool destroyGameObject = false){
        yield return new WaitForSeconds(time);
        RemoveAdditionalTarget(target);
        if(destroyGameObject) Destroy(target.gameObject);
    }



    // Private Functions


    private float CalculateFramingDistance(Camera camera, Vector3 boundingDimensions){
        // Returns the ideal distance to place the camera to frame all players, from the center of the box

        Vector2 frameDimensions = new Vector2(boundingDimensions.x, boundingDimensions.y);
        float dist;

        // Determine whether the constraining dimension is horizontal or vertical
        if (frameDimensions.x/frameDimensions.y >= camera.aspect){
            // Constrain by width
            float hFOV = Camera.VerticalToHorizontalFieldOfView(camera.fieldOfView, camera.aspect);
            dist = frameDimensions.x / (2 * Mathf.Tan(Mathf.Deg2Rad * hFOV / 2));
        }
        else{
            // Constrain by height
            float vFOV = camera.fieldOfView;
            dist = frameDimensions.y / (2 * Mathf.Tan(Mathf.Deg2Rad * vFOV / 2));
        }

        // Add the distance from the center of the bounding box to the frame (the front face)
        dist += boundingDimensions.z / 2;

        return dist;
    }


    private void ResizeFrameToAspectRatio(ref Vector3 maxDimensionsLocal, ref Vector3 minDimensionsLocal, float aspectRatio){
        // Resizes the given box to match the camera's aspect ratio, keeping the old volume inside of the new dimensions
        // If the new frame is outside of the bounding area, shift it back inside

        Vector3 boxSize = maxDimensionsLocal - minDimensionsLocal;
        Vector3 boxCenter = (maxDimensionsLocal + minDimensionsLocal) / 2;

        float currAspect = boxSize.x / boxSize.y;


        if (currAspect >= aspectRatio){
            // Current box is not tall enough; stretch vertically, constant horizontal
            float newHeight = boxSize.x / aspectRatio;
            boxSize = new Vector3(boxSize.x, newHeight, boxSize.z);
        }
        else{
            // Current box is not wide enough; stretch horizontally, constant vertical
            float newWidth = boxSize.y * aspectRatio;
            boxSize = new Vector3(newWidth, boxSize.y, boxSize.z);
        }


        // Set referenced max and min corners
        maxDimensionsLocal = boxCenter + (boxSize / 2);
        minDimensionsLocal = boxCenter - (boxSize / 2);
    }


    private void ClampBoxDimensionsToBounds(ref Vector3 maxDimensionsLocal, ref Vector3 minDimensionsLocal){
        // Clamps the min and max dimensions of the box surrounding the player targets.
        // Box min and max dimension points are relative to the camera

        Vector3 anchorPositionLocal = transform.InverseTransformPoint(anchorTransform.position);

        Vector3 maxDimensionsRelativeToAnchor = maxDimensionsLocal - anchorPositionLocal;
        Vector3 minDimensionsRelativeToAnchor = minDimensionsLocal - anchorPositionLocal;


        // Clamp horizontal direction
        Vector3 maxHorizontalClamped = Vector3.Project(maxDimensionsRelativeToAnchor, Vector3.right).ClampMagnitude(0, targetBoundsHorizontal);
        Vector3 minHorizontalClamped = Vector3.Project(minDimensionsRelativeToAnchor, Vector3.right).ClampMagnitude(0, targetBoundsHorizontal);

        // Clamp target bounds in the vertical direction
        Vector3 maxVerticalClamped = Vector3.Project(maxDimensionsRelativeToAnchor, Vector3.up).ClampMagnitude(0, targetBoundsVertical);
        Vector3 minVerticalClamped = Vector3.Project(minDimensionsRelativeToAnchor, Vector3.up).ClampMagnitude(0, targetBoundsVertical);


        // Update target box dimensions to match the clamped values
        maxDimensionsLocal = anchorPositionLocal + maxHorizontalClamped + maxVerticalClamped;
        minDimensionsLocal = anchorPositionLocal + minHorizontalClamped + minVerticalClamped;
    }

    private void ShiftBoxInsideBounds(ref Vector3 maxDimensionsLocal, ref Vector3 minDimensionsLocal){
        // Clamps the min and max dimensions of the box surrounding the player targets.
        // Box min and max dimension points are relative to the camera

        Vector3 anchorPositionLocal = transform.InverseTransformPoint(anchorTransform.position);

        Vector3 boxCenterRelativeToAnchor = ((maxDimensionsLocal + minDimensionsLocal) / 2) - anchorPositionLocal;
        Vector3 boxSize = maxDimensionsLocal - minDimensionsLocal;


        // If the box is bigger than one of the bounding area's dimensions, scale the box to fit inside while maintaining aspect ratio
        float boxWidthBoundaryFraction = boxSize.x / (targetBoundsHorizontal * 2);
        float boxHeightBoundaryFraction = boxSize.y / (targetBoundsVertical * 2);

        float maxBoundaryFraction = Mathf.Max(boxWidthBoundaryFraction, boxHeightBoundaryFraction);
        if(maxBoundaryFraction > 1) boxSize = boxSize / maxBoundaryFraction;


        // Clamp the center of the box to fit the whole box within the bounds
        float maxHorizontalDist = targetBoundsHorizontal - (boxSize.x/2);
        float maxVerticalDist = targetBoundsVertical - (boxSize.y/2);

        float boxCenterHorizontalClamped = Mathf.Clamp(boxCenterRelativeToAnchor.x, -maxHorizontalDist, maxHorizontalDist);
        float boxCenterVerticalClamped = Mathf.Clamp(boxCenterRelativeToAnchor.y, -maxVerticalDist, maxVerticalDist);

        boxCenterRelativeToAnchor = new Vector3(boxCenterHorizontalClamped, boxCenterVerticalClamped, boxCenterRelativeToAnchor.z);

        // Update target box dimensions to match the clamped values
        maxDimensionsLocal = anchorPositionLocal + boxCenterRelativeToAnchor + (boxSize/2);
        minDimensionsLocal = anchorPositionLocal + boxCenterRelativeToAnchor - (boxSize/2);
    }



    private bool IsLineOfSightClear(Vector3 targetPos, Vector3 cameraPos, float castDistance, float sphereRadius, LayerMask obstructingLayers){
        // Casts a sphere from the target to the camera to determine if there are any obstructions directly in the way
        Ray LOSRay = new Ray(targetPos, cameraPos - targetPos);

        if(Physics.SphereCast(LOSRay, sphereRadius, out RaycastHit hitInfo, castDistance, obstructingLayers)){
            return false;
        }
        else{
            return true;
        }
    }


    private Vector3 GetUnobstructedCameraPosition(Vector3 targetPos, Vector3 cameraPos, float castDistance, float sphereRadius, LayerMask obstructingLayers){
        // Performs a spherecast from the player to the ideal camera position to determine where to place the camera without intersecting geometry
        Ray LOSRay = new Ray(targetPos, cameraPos - targetPos);

        if(Physics.SphereCast(LOSRay, sphereRadius, out RaycastHit hitInfo, castDistance, obstructingLayers)){
            Vector3 sphereCenter = hitInfo.point + (hitInfo.normal * sphereRadius);
            return sphereCenter;
        }
        else{
            return cameraPos;
        }
    }

    
    private Vector3 ValidateCameraPosition(Vector3 targetPos, Vector3 cameraPos, float sphereRadius, LayerMask invalidLayers){
        // Check if the camera's bubble is intersecting any geometry it shouldn't collide with. If it is, calculate a valid position

        if(Physics.OverlapSphere(cameraPos, sphereRadius, invalidLayers).Length > 0){
            // Camera is currently intersecting geometry; perform spherecast from player to camera to get furthest valid position
            float castDistance = (cameraPos - targetPos).magnitude;
            Vector3 validCameraPos = GetUnobstructedCameraPosition(targetPos, cameraPos, castDistance, sphereRadius, invalidLayers);

            return validCameraPos;
        }
        else{
            // Camera position is valid
            return cameraPos;
        }
    }


    private void UpdateCursorLock(){
        // Lock cursor if player clicks on game window, unlock if escape is pressed
        if (Cursor.lockState == CursorLockMode.None && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
        }
        else if (Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
        }
    }



    public void SetParameters(CameraParametersContainer newParameters){
        anchorTransform = newParameters.anchorTransform;

        SetLookAngles(newParameters.horizontalAngle, newParameters.verticalAngle);
        UpdateCameraDirection();

        UpdateFOV(newParameters.cameraFOV);

        horizontalPaddingDistance = newParameters.horizontalPaddingDistance;
        verticalPaddingDistance = newParameters.verticalPaddingDistance;

        targetBoundsHorizontal = newParameters.maxDistanceHorizontal;
        targetBoundsVertical = newParameters.maxDistanceVertical;

        springFrequency = newParameters.springFrequency;
        springDamping = newParameters.springDamping;
    }

    public void SetParameters(CameraParametersContainer newParameters, CameraTransitionParameters transitionParameters){

        anchorTransform = newParameters.anchorTransform;

        StartCoroutine(TransitionRotation(
            newParameters.horizontalAngle, newParameters.verticalAngle, 
            transitionParameters.angleTransitionCurve, transitionParameters.angleTransitionTime));


        StartCoroutine(TransitionFOV(newParameters.cameraFOV, transitionParameters.fovTransitionCurve, transitionParameters.fovTransitionTime));

        StartCoroutine(TransitionPadding(newParameters.horizontalPaddingDistance, newParameters.verticalPaddingDistance, 
            transitionParameters.paddingTransitionCurve, transitionParameters.paddingTransitionTime));


        targetBoundsHorizontal = newParameters.maxDistanceHorizontal;
        targetBoundsVertical = newParameters.maxDistanceVertical;


        springFrequency = newParameters.springFrequency;
        springDamping = newParameters.springDamping;
    }



    public void SetLookAngles(float horizontal, float vertical){
        horizontalAngle = horizontal;
        verticalAngle = vertical;

        UpdateCameraDirection();
    }
    public void UpdateCameraDirection(){
        rb.MoveRotation(Quaternion.Euler(verticalAngle, horizontalAngle, 0));
        transform.rotation = Quaternion.Euler(verticalAngle, horizontalAngle, 0);
    }

    public void UpdateFOV(float FOV){
        cameraFOV = FOV;
        mainCamera.fieldOfView = cameraFOV;
    }


    public void AddCameraShake(float traumaAmount){
        cameraShakeManager.AddCameraShake(traumaAmount);
    }



    private IEnumerator TransitionRotation(float finalHorizontal, float finalVertical, AnimationCurve transitionCurve, float transitionTime){
        // Transitions the camera's horizontal and vertical rotation from its current values to given values over a given time period, 
        // following a curve from 0 to 1 (start to end)

        float initialHorizontal = horizontalAngle;
        float initialVertical = verticalAngle;

        float currTime = 0;
        while(currTime < transitionTime){
            currTime += Time.fixedDeltaTime;

            float gradient = transitionCurve.Evaluate(currTime / transitionTime);

            horizontalAngle = gradient.Map(0, 1, initialHorizontal, finalHorizontal);
            verticalAngle = gradient.Map(0, 1, initialVertical, finalVertical);

            UpdateCameraDirection();

            yield return new WaitForFixedUpdate();
        }

        SetLookAngles(finalHorizontal, finalVertical);
    }

    private IEnumerator TransitionFOV(float finalFOV, AnimationCurve transitionCurve, float transitionTime){
        // Transitions the camera's FOV from its current value to a given value over a given time period, 
        // following a curve from 0 to 1 (start to end)

        float initialFOV = cameraFOV;

        float currTime = 0;
        while(currTime < transitionTime){
            currTime += Time.fixedDeltaTime;

            float gradient = transitionCurve.Evaluate(currTime / transitionTime);
            float currFOV = gradient.Map(0, 1, initialFOV, finalFOV);

            UpdateFOV(currFOV);

            yield return new WaitForFixedUpdate();
        }

        UpdateFOV(finalFOV);
    }

    private IEnumerator TransitionPadding(float finalHorizontal, float finalVertical, AnimationCurve transitionCurve, float transitionTime){
        // Transitions the camera's horizontal and vertical padding from its current values to given values over a given time period, 
        // following a curve from 0 to 1 (start to end)

        float initialHorizontal = horizontalPaddingDistance;
        float initialVertical = verticalPaddingDistance;

        float currTime = 0;
        while(currTime < transitionTime){
            currTime += Time.fixedDeltaTime;

            float gradient = transitionCurve.Evaluate(currTime / transitionTime);

            horizontalPaddingDistance = gradient.Map(0, 1, initialHorizontal, finalHorizontal);
            verticalPaddingDistance = gradient.Map(0, 1, initialVertical, finalVertical);

            yield return new WaitForFixedUpdate();
        }

        horizontalPaddingDistance = finalHorizontal;
        verticalPaddingDistance = finalVertical;
    }
}
