using UnityEngine;

namespace Systems.Phone
{
    public enum MessageTemplateKind
    {
        Invite = 0,
        HangOut = 1,
        Weird = 2
    }

    [CreateAssetMenu(fileName = "MessageTemplate", menuName = "Phone/Message Template")]
    public class MessageTemplateSO : ScriptableObject
    {
        public MessageTemplateKind kind = MessageTemplateKind.Invite;
        [TextArea(2, 4)] public string body = "u coming to the party friday?";
        [Tooltip("Local-only affinity change for later relationship systems.")]
        public float affinityDelta;
        [Tooltip("When true, sending this text replicates an invite to the server for party spawn bias.")]
        public bool appliesInvite;
        [Range(0f, 100f)]
        [Tooltip("Bonus added to guest arrival chance when this invite is pending.")]
        public float arrivalBonus = 80f;
        [Tooltip("Optional canned NPC reply shown locally after send.")]
        [TextArea(1, 3)] public string localReply = "bet";
    }
}
