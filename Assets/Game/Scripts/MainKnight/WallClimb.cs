using UnityEngine;


[RequireComponent(typeof(Rigidbody))]
public class WallClimb : MonoBehaviour
{
    [Header("Wall detection")]
    public LayerMask wallLayer;             
    public float wallCheckDistance = 0.7f;
    public Transform orientation;           

    [Header("Keybinds")]
    public KeyCode grabKey = KeyCode.LeftAlt;
    public KeyCode jumpKey = KeyCode.Space;

    [Header("Cling settings")]
    public float stickDuration = 3f;         
    public float slideStartSpeed = 0.5f;    
    public float slideAcceleration = 2f;    
    public float maxSlideSpeed = 4f;        
    public float wallStickForce = 5f;        

    [Header("Wall jump")]
    public Transform cameraTransform;       
    public float wallJumpForce = 12f;        
    public float wallJumpUpBoost = 3f;       
    public float reGrabCooldown = 0.3f;

    [Header("Refs")]
    public KnightMovement movement;

    private Rigidbody rb;
    private bool isClinging;
    private bool isSliding;                  
    private float clingTimer;
    private float currentSlideSpeed;
    private Vector3 wallNormal;
    private float cooldownTimer;

    public bool IsClinging => isClinging;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        bool touchingWall = CheckWall(out RaycastHit hit);
        bool grounded = movement != null && movement.IsGrounded();
        bool holdingGrab = Input.GetKey(grabKey);

        
        if (!isClinging && touchingWall && !grounded && holdingGrab && cooldownTimer <= 0f)
        {
            StartCling(hit.normal);
        }

        if (isClinging)
        {
            bool stillTouchingWall = CheckWall(out RaycastHit hit2);

            if (!stillTouchingWall || grounded)
            {
                StopCling();
            }
            else
            {
                wallNormal = hit2.normal;

                if (clingTimer > 0f)
                    clingTimer -= Time.deltaTime;

                
                bool shouldStick = holdingGrab && clingTimer > 0f;

                if (!shouldStick && !isSliding)
                {
                    
                    isSliding = true;
                    currentSlideSpeed = slideStartSpeed;
                }

               
                if (Input.GetKeyDown(jumpKey))
                {
                    WallJump();
                    return;
                }
            }
        }
    }

    void FixedUpdate()
    {
        if (!isClinging) return;

        if (!isSliding)
        {
            
            rb.linearVelocity = Vector3.zero;
        }
        else
        {
           
            currentSlideSpeed = Mathf.Min(currentSlideSpeed + slideAcceleration * Time.fixedDeltaTime, maxSlideSpeed);
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -currentSlideSpeed, rb.linearVelocity.z);
        }

        rb.AddForce(-wallNormal * wallStickForce, ForceMode.Force);
    }

    private bool CheckWall(out RaycastHit hit)
    {
        Vector3 origin = transform.position;
        Vector3 fwd = orientation != null ? orientation.forward : transform.forward;
        Vector3 right = orientation != null ? orientation.right : transform.right;

        Vector3[] directions = { fwd, right, -right };

        foreach (var dir in directions)
        {
            if (Physics.Raycast(origin, dir, out hit, wallCheckDistance, wallLayer))
                return true;
        }

        hit = default;
        return false;
    }

    private void StartCling(Vector3 normal)
    {
        isClinging = true;
        isSliding = false;
        clingTimer = stickDuration;
        wallNormal = normal;

        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;

        if (movement != null)
            movement.enabled = false;
    }

    private void StopCling()
    {
        isClinging = false;
        isSliding = false;
        cooldownTimer = reGrabCooldown;

        rb.useGravity = true;

        if (movement != null)
            movement.enabled = true;
    }

    private void WallJump()
    {
        Vector3 dir = cameraTransform != null ? cameraTransform.forward.normalized : transform.forward;
        Vector3 jumpVel = dir * wallJumpForce + Vector3.up * wallJumpUpBoost;

        rb.linearVelocity = Vector3.zero;
        rb.AddForce(jumpVel, ForceMode.Impulse);

        StopCling();
    }
}