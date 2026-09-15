using System;
using UnityEngine;
using TMPro; 

public class KnightMovment : MonoBehaviour
{
    [Header("Movement")]
    private float moveSpeed;
    public float walkSpeed;
    public float sprintSpeed;
    public float groundDrag;

    [Header("jump")]
    public float jumpForce;
    public float jumpCooldown;
    public float airMultiplier;
    bool readyToJump;
    
    [Header("crouch")]
    public CapsuleCollider capsule;
    public float crouchHeight = 1f;  
    public float startHeight;
    public Vector3 startCenter;
    public float crouchSpeed;
    public float crouchYScale;
    public float startYScale;
    private bool isCrouching;

    [Header("Model Rotation")]
    public Transform model;
    public float rotationSpeed = 12f;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;
    public KeyCode slideKey = KeyCode.LeftShift;
    
    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    bool grounded;

    [Header("Slope Handling")]
    public float maxSlopeAngel;
    private RaycastHit slopeHit; 
    private bool exitingSlope;

    [Header("UI Elements")]
    public TextMeshProUGUI speedText;  
    public TextMeshProUGUI groundText;

    public Transform orientation;

    [Header("Slide setting")]
    public float slideSpeed = 12f;     
    public float slideDuration = 0.6f;   
    public float slideCooldown = 0.4f; 
    

    private float slideTimer;
    private float slideCooldownTimer;
    private bool isSliding;
    private bool shiftConsumed;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDir;

    Rigidbody rb;
    
    public MovementState state;
    public enum MovementState
    {
        walking,
        sprinting,
        air,
        croaching,
        sliding
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    
        startHeight = capsule.height;
        startCenter = capsule.center;

        readyToJump = true; 

        startYScale = transform.localScale.y;
    }
    
    void Update()
    {
        grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, whatIsGround);
        
        MyInput(); 
        SlideHandler();
        SpeedControl();
        StateHandler();
        
        if (speedText != null)
        {
            float currentSpeed = rb.linearVelocity.magnitude; 
            speedText.text = "Speed: " + currentSpeed.ToString("F2"); 
        }

        if (groundText != null)
        {
            groundText.text = "Grounded: " + grounded.ToString();
        }

        if(grounded)
            rb.linearDamping = groundDrag;
        else 
            rb.linearDamping = 0;
    }

    private void FixedUpdate()
    {
        MovePlayer();
        RotateModel();
    }
    private void RotateModel()
    {
        if (model == null) return;

        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if (flatVel.sqrMagnitude < 0.01f) return;

        Quaternion targetRot = Quaternion.LookRotation(flatVel.normalized);
        model.rotation = Quaternion.Slerp(model.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
    }
    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");


        if(Input.GetKeyDown(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }


    }

    private void SlideHandler()
    {
        if (!Input.GetKey(slideKey))
            shiftConsumed = false;

        if (slideCooldownTimer > 0f)
            slideCooldownTimer -= Time.deltaTime;

        if (Input.GetKeyDown(slideKey) && !shiftConsumed && !isSliding && slideCooldownTimer <= 0f && grounded)
        {
            isSliding = true;
            slideTimer = slideDuration;
            shiftConsumed = true;
        }

        if (isSliding)
        {
            slideTimer -= Time.deltaTime;

            if (slideTimer <= 0f)
            {
                isSliding = false;
                slideCooldownTimer = slideCooldown;
            }
        }
    }

    private void StateHandler()
    {
        if (Input.GetKeyDown(crouchKey) && grounded && !isSliding)
        {
            isCrouching = true;
            ApplyCrouchCollider(crouchHeight);
            if (model != null) model.localScale = new Vector3(1f, crouchYScale, 1f);
        }

        if (Input.GetKeyUp(crouchKey) && isCrouching)
        {

            float heightDiff = startHeight - crouchHeight;
            bool headBlocked = Physics.Raycast(transform.position, Vector3.up, heightDiff + 0.1f, whatIsGround);

            if (!headBlocked)
            {
                isCrouching = false;
                ApplyCrouchCollider(startHeight);
                if (model != null) model.localScale = Vector3.one;
            }

        }

        if (isSliding)
        {
            state = MovementState.sliding;
            moveSpeed = slideSpeed;
        }
        else if (isCrouching)
        {
            state = MovementState.croaching;
            moveSpeed = crouchSpeed;
        }
        else if (grounded && Input.GetKey(sprintKey))
        {
            state = MovementState.sprinting;
            moveSpeed = sprintSpeed;
        }
        else if (grounded)
        {
            state = MovementState.walking;
            moveSpeed = walkSpeed;            
        }
        else
        {
            state = MovementState.air;
        }
    }


    private void ApplyCrouchCollider(float targetHeight)
    {
        float bottomOffset = capsule.center.y - capsule.height * 0.5f;
        capsule.height = targetHeight;
        capsule.center = new Vector3(startCenter.x, bottomOffset + targetHeight * 0.5f, startCenter.z);
    }

    private void MovePlayer()
    {
        if (isSliding)
        {
            Vector3 slideDir = orientation.forward;
            rb.linearVelocity = new Vector3(slideDir.x * slideSpeed, rb.linearVelocity.y, slideDir.z * slideSpeed);
            rb.useGravity = !OnSlope();
            return;
        }

        //slope
        if (OnSlope() && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDir() * moveSpeed * 20f, ForceMode.Force);

            if(rb.linearVelocity.y > 0)
            {
                rb.AddForce(Vector2.down * 80f, ForceMode.Force);
            }
        }
        moveDir = orientation.forward * verticalInput + orientation.right * horizontalInput;
        //ground
        if(grounded)
            rb.AddForce(moveDir.normalized * moveSpeed * 10f, ForceMode.Force);
        //air
        else if(!grounded)
            rb.AddForce(moveDir.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
        //turn gravite off if slope
        rb.useGravity = !OnSlope();
    }

    private void SpeedControl()
    {
        if (isSliding) return;

        //limiting speed for slope
        if (OnSlope() && !exitingSlope)
        {
            if(rb.linearVelocity.magnitude > moveSpeed)
            {
                rb.linearVelocity = rb.linearVelocity.normalized * moveSpeed;
            }
        }
        else
        {
            Vector3 flatVelo = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            if (flatVelo.magnitude > moveSpeed)
            {
                Vector3 limitedVel = flatVelo.normalized * moveSpeed;
                rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
            }
        }


        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if(flatVel.magnitude > moveSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * moveSpeed;
            rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
        }
    }

    private void Jump()
    {
        if (grounded)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
            exitingSlope =true;
        }

    }

    private void ResetJump()
    {
        readyToJump = true;
         exitingSlope = false;
    }

    private bool OnSlope()
    {
        if(Physics.Raycast(transform.position, Vector3.down, out slopeHit, playerHeight * 0.5f + 0.3f))
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            return angle < maxSlopeAngel && angle != 0;
        }
        
        return false;
    }

    private Vector3 GetSlopeMoveDir()
    {
        return Vector3.ProjectOnPlane(moveDir, slopeHit.normal).normalized;
    }

    public bool IsGrounded()
    {
        return grounded;
    }


}