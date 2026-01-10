using UnityEngine;

namespace Unity.FPSSample_2
{
    public enum WeaponType
    {
        Hitscan,
        Projectile
    }

    public enum ReticleType
    {
        Cross,
        TCross,
        OpenCircular,
        CircularCross
    }

    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "FPS Sample/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("General")] public string WeaponName = "Assault Rifle";
        public WeaponType Type = WeaponType.Hitscan;
        public ReticleType ReticleType = ReticleType.TCross;

        [Header("Firing Mechanics")] [Tooltip("Shots per second")]
        public float CooldownInMs = 10f;

        public float Damage = 15f;

        [Header("Hitscan Properties")] [Tooltip("Max range for raycast-based weapons.")]
        public float HitscanRange = 100f;

        [Header("Ammo & Reloading")] public int MagazineSize = 30;
        public float ReloadTime = 2.0f; // Time in seconds

        [Header("Projectile Properties")] [Tooltip("The ghost prefab for the projectile to be spawned.")]
        public GhostSpawner.GhostReference ProjectileGhostPrefab;

        public GhostSpawner.GhostReference ProjectileHitVfxPrefab;
        public GhostSpawner.GhostReference MuzzleFlashVfxPrefab;
        public SoundDef WeaponFireSfx;
        public SoundDef WeaponReloadSfx;

        public ProjectileBehavior Behavior = ProjectileBehavior.DirectDamage;
        public float AoeRadius = 5f;
        public float ProjectileSpeed = 30f;
    }

    public enum ProjectileBehavior
    {
        DirectDamage,
        AreaOfEffect
    }
}