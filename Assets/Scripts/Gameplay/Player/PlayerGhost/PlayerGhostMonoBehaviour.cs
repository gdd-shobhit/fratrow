
namespace Unity.FPSSample_2
{
    public abstract class PlayerGhostMonoBehaviour : GhostMonoBehaviour
    {
        public PlayerGhost PlayerGhost
        {
            get => m_PlayerGhost;
        }

        private PlayerGhost m_PlayerGhost;

        public virtual void Awake()
        {
            GetRequiredComponent(out m_PlayerGhost);
        }
    }
}