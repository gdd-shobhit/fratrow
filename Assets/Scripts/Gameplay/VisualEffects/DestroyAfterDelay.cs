using UnityEngine;

namespace Unity.FPSSample_2
{
    public class DestroyAfterDelay : MonoBehaviour
    {
        public float Lifetime = 1f;

        private void Start()
        {
            Destroy(gameObject, Lifetime);
        }
    }
}