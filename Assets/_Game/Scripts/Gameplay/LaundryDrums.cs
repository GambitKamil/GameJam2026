using UnityEngine;

public sealed class LaundryDrums : MonoBehaviour
{
    [SerializeField] private Transform[] drums;

    private void Update()
    {
        for (int index = 0; index < drums.Length; index++)
        {
            float speed = 43f + 13f * index;
            drums[index].Rotate(0f, 0f, speed * Time.deltaTime, Space.Self);
        }
    }
}
