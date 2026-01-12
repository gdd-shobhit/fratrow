using UnityEngine;

public enum PlayerClass
{
    President,
    SocialChair,
    Chad,
    RichGuy
}

public class UserCharacter : BaseCharacter
{
    [Header("Guest Data")]
    [SerializeField] private PlayerClassSO userData;
    public PlayerClass currentClass;
    protected override void InitializeCharacter()
    {
        base.InitializeCharacter();
        // initialize user characterstics
        currentClass = PlayerClass.President;
    }
}
