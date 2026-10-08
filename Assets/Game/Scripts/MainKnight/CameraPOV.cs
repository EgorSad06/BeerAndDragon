using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Movment : MonoBehaviour
{
    public float sensX;
    public float sensY;

    public Transform orientation;

    [Header("Recoil (отдача от оружия)")]
    public float recoilSnappiness = 25f;
    public float recoilReturnSpeed = 10f;

    float xRotation;
    float yRotation;

    // Поворот камеры по горизонтали -- им пользуется скейт, чтобы камера вставала за спину
    public float Yaw
    {
        get => yRotation;
        set => yRotation = value;
    }

    Vector2 recoilCurrent;
    Vector2 recoilTarget;

    private void Start()
    {
        Cursor.lockState = CursorLockMode. Locked;
        Cursor.visible = false;
    }


    private void Update()
    {
        if (PlayerInputLock.Locked) return; // открыт инвентарь или игрок мёртв

        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;

        yRotation += mouseX;
        xRotation -= mouseY;

        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        recoilCurrent = Vector2.Lerp(recoilCurrent, recoilTarget, Time.deltaTime * recoilSnappiness);
        recoilTarget = Vector2.Lerp(recoilTarget, Vector2.zero, Time.deltaTime * recoilReturnSpeed);

        float pitch = Mathf.Clamp(xRotation + recoilCurrent.x, -90f, 90f);
        transform.rotation = Quaternion.Euler(pitch, yRotation + recoilCurrent.y, 0);
        orientation.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    // Вызывается оружием при выстреле: pitch -- вверх, yaw -- в сторону (градусы)
    public void AddRecoil(float pitch, float yaw)
    {
        recoilTarget += new Vector2(-pitch, yaw);
    }


}
