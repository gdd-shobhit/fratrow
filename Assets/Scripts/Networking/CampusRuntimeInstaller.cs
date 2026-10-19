using UnityEngine;

namespace Systems.Campus
{
    /// <summary>
    /// Placeholder installer kept for scene-load side effects; campus systems attach via NetworkedGameManager.Awake.
    /// </summary>
    public class CampusRuntimeInstaller : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void WarmDatabases()
        {
            NamedNpcDatabase.EnsureInitialized();
            Systems.Phone.PhoneContactDatabase.EnsureInitialized();
            CampusPersistence.ApplyToLocalPhones();
        }
    }
}
