using UnityEngine;

// Покачивание оружия в руках: отстаёт от мыши (sway) и качается при беге (bob).
// Вешается на WeaponHolder (дочерний объект камеры).
public class WeaponSway : MonoBehaviour
{
    [Header("Sway (от мыши)")]
    public float swayAmount = 1.5f;
    public float maxSway = 6f;
    public float swaySmooth = 8f;

    [Header("Bob (от ходьбы)")]
    public Rigidbody playerBody;            // найдётся сам
    public KnightMovment movement;          // найдётся сам
    public float bobFrequency = 1.4f;       // шагов на метр скорости... примерно
    public float bobAmountX = 0.015f;
    public float bobAmountY = 0.02f;
    public float bobSmooth = 10f;

    private Vector3 restPos;
    private Quaternion restRot;
    private float bobTimer;
    private Vector3 bobOffset;

    void Start()
    {
        restPos = transform.localPosition;
        restRot = transform.localRotation;

        Transform root = transform.root;
        if (playerBody == null) playerBody = root.GetComponent<Rigidbody>();
        if (movement == null) movement = root.GetComponent<KnightMovment>();
    }

    void Update()
    {
        bool locked = PlayerInputLock.Locked; // в инвентаре мышь двигает курсор, а не камеру
        float mx = locked ? 0f : Mathf.Clamp(-Input.GetAxisRaw("Mouse X") * swayAmount, -maxSway, maxSway);
        float my = locked ? 0f : Mathf.Clamp(-Input.GetAxisRaw("Mouse Y") * swayAmount, -maxSway, maxSway);
        Quaternion swayRot = Quaternion.Euler(-my, mx, mx * 0.5f);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, restRot * swayRot, Time.deltaTime * swaySmooth);

        Vector3 targetBob = Vector3.zero;
        if (playerBody != null)
        {
            Vector3 flat = playerBody.linearVelocity;
            flat.y = 0f;
            bool grounded = movement == null || movement.IsGrounded();
            float speed = flat.magnitude;
            if (grounded && speed > 0.5f)
            {
                bobTimer += Time.deltaTime * speed * bobFrequency;
                targetBob = new Vector3(Mathf.Cos(bobTimer) * bobAmountX, Mathf.Abs(Mathf.Sin(bobTimer)) * -bobAmountY, 0f);
            }
            // В воздухе оружие чуть уезжает вверх/вниз от вертикальной скорости
            if (!grounded)
                targetBob.y = Mathf.Clamp(-playerBody.linearVelocity.y * 0.004f, -0.05f, 0.05f);
        }

        bobOffset = Vector3.Lerp(bobOffset, targetBob, Time.deltaTime * bobSmooth);
        transform.localPosition = restPos + bobOffset;
    }
}
