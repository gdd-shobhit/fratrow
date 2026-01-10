namespace Unity.FPSSample_2
{
    public class GhostVisualEffect : GhostMonoBehaviour, IUpdateServer
    {
        public float Lifetime = 3f;
        private float _time;
        
        public void UpdateServer(float deltaTime)
        {
            _time += deltaTime;

            if (_time >= Lifetime)
            {
                GhostGameObject.DestroyEntity();
            }
        }
    }
}