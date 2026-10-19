using System;
using System.Collections.Generic;
using Systems.Campus;
using Systems.Phone;
using UnityEngine;

namespace Systems.Campus
{
    [Serializable]
    public class CampusSaveData
    {
        public int dayOfWeek;
        public int weekIndex;
        public List<ContactSaveEntry> contacts = new List<ContactSaveEntry>();
    }

    [Serializable]
    public class ContactSaveEntry
    {
        public string npcId;
        public bool unlocked;
        public bool recruited;
        public float affinity;
    }

    /// <summary>
    /// Thin JSON persistence for campus roster + calendar day (local file, not NGO).
    /// </summary>
    public static class CampusPersistence
    {
        const string FileName = "campus_save.json";

        static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static void SaveFromServer()
        {
            var data = new CampusSaveData();
            var cal = NetworkedCampusCalendar.Instance;
            if (cal != null)
            {
                data.dayOfWeek = cal.DayOfWeek;
                data.weekIndex = cal.WeekIndex;
            }

            var roster = NetworkedContactRoster.Instance;
            NamedNpcDatabase.EnsureInitialized();
            foreach (var npc in NamedNpcDatabase.All)
            {
                var entry = new ContactSaveEntry { npcId = npc.npcId };
                if (roster != null && roster.TryGet(npc.npcId, out var state))
                {
                    entry.unlocked = state.Unlocked;
                    entry.recruited = state.Recruited;
                    entry.affinity = state.Affinity;
                }
                else
                {
                    var contact = npc.ResolveContact();
                    entry.unlocked = contact != null && contact.startsUnlocked;
                }

                data.contacts.Add(entry);
            }

            try
            {
                System.IO.File.WriteAllText(Path, JsonUtility.ToJson(data, true));
                Debug.Log($"[CampusPersistence] Saved to {Path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CampusPersistence] Save failed: {e.Message}");
            }
        }

        public static CampusSaveData Load()
        {
            try
            {
                if (!System.IO.File.Exists(Path))
                {
                    return null;
                }

                return JsonUtility.FromJson<CampusSaveData>(System.IO.File.ReadAllText(Path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CampusPersistence] Load failed: {e.Message}");
                return null;
            }
        }

        public static void ApplyToLocalPhones()
        {
            var data = Load();
            if (data == null)
            {
                return;
            }

            foreach (var phone in UnityEngine.Object.FindObjectsByType<PhoneController>(FindObjectsSortMode.None))
            {
                foreach (var entry in data.contacts)
                {
                    var npc = NamedNpcDatabase.GetById(entry.npcId);
                    var contact = npc?.ResolveContact();
                    if (contact == null)
                    {
                        continue;
                    }

                    var state = phone.GetState(contact.Id);
                    if (state == null)
                    {
                        continue;
                    }

                    state.unlocked = entry.unlocked;
                    state.affinity = entry.affinity;
                }

                phone.EnsureStates();
            }
        }
    }
}
