using Systems.Campus;
using UnityEngine;

public class Interact_Start : MonoBehaviour, IInteractable
{
    public void Interact()
    {
        var cal = NetworkedCampusCalendar.Instance;
        if (cal != null && cal.Phase == GamePhase.Weekday)
        {
            Debug.Log("[Interact_Start] Use phone Ready/Start Weekend during weekday phase.");
            return;
        }

        NetworkedPartyManager.Instance?.StartPartyServer();
    }
}
