using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPSSample_2
{
    [CreateAssetMenu(fileName = "WeaponRegistry", menuName = "FPS Sample/Weapon Registry")]
    public class WeaponRegistry : ScriptableObject
    {
        public List<WeaponData> Weapons;

        public WeaponData GetWeaponData(uint weaponID)
        {
            if (weaponID < Weapons.Count)
            {
                return Weapons[(int)weaponID];
            }

            return null; // or a default weapon
        }
    }
}