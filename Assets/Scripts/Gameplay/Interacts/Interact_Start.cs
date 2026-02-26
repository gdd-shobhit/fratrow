using UnityEngine;

public class Interact_Start : MonoBehaviour, IInteractable
{
    public void Interact()
    {
        PartyManager.Instance.RequestStartParty();
    }
}
