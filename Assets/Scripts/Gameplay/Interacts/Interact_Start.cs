using UnityEngine;

public class Interact_Start : MonoBehaviour, IInteractable
{
    public void Interact()
    {
        NetworkedPartyManager.Instance.StartPartyServer();
    }
}
