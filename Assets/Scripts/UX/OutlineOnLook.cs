using Unity.VisualScripting;
using UnityEngine;

public class OutlineOnLook : MonoBehaviour
{
    public Material mat;
    public int indexOfMat = 1;

    private void Awake()
    {
        mat = GetComponent<Renderer>().materials[indexOfMat];
    }

    public void Outline(bool show)
    {
        mat.SetFloat("_ShowOutline", show ? 1 : 0);
    }
}
