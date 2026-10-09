using UnityEngine;

// Для вспышки выстрела: при появлении случайный поворот вокруг оси ствола и размер,
// чтобы вспышки не были одинаковыми.
public class RandomSpin : MonoBehaviour
{
    public Vector2 scaleRange = new Vector2(0.8f, 1.25f);

    void OnEnable()
    {
        transform.localRotation *= Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        transform.localScale *= Random.Range(scaleRange.x, scaleRange.y);
    }
}
