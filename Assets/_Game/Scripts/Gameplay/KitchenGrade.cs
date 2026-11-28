using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class KitchenGrade : MonoBehaviour
{
    [SerializeField] private Shader grade;
    private Material material;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (grade == null || !grade.isSupported)
        {
            Graphics.Blit(source, destination);
            return;
        }

        if (material == null)
            material = new Material(grade) { hideFlags = HideFlags.HideAndDontSave };
        Graphics.Blit(source, destination, material);
    }

    private void OnDisable()
    {
        if (material != null)
            Destroy(material);
    }
}
