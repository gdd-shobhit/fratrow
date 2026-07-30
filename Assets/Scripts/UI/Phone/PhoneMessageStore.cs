using System;
using System.Collections.Generic;

namespace Systems.Phone
{
    [Serializable]
    public class PhoneMessage
    {
        public string body;
        public bool fromPlayer;
        public long ticks;
    }

    /// <summary>Local-only message history. Never networked.</summary>
    public class PhoneMessageStore
    {
        readonly Dictionary<string, List<PhoneMessage>> _threads = new Dictionary<string, List<PhoneMessage>>();

        public IReadOnlyList<PhoneMessage> GetThread(SerializableGuid contactId)
        {
            var key = contactId.ToHexString();
            if (!_threads.TryGetValue(key, out var list))
            {
                list = new List<PhoneMessage>();
                _threads[key] = list;
            }
            return list;
        }

        public void Append(SerializableGuid contactId, string body, bool fromPlayer)
        {
            var key = contactId.ToHexString();
            if (!_threads.TryGetValue(key, out var list))
            {
                list = new List<PhoneMessage>();
                _threads[key] = list;
            }

            list.Add(new PhoneMessage
            {
                body = body,
                fromPlayer = fromPlayer,
                ticks = DateTime.UtcNow.Ticks
            });
        }

        public bool HasMessages(SerializableGuid contactId)
        {
            return _threads.TryGetValue(contactId.ToHexString(), out var list) && list.Count > 0;
        }
    }
}
