using UnityEngine;
using UnityEditor;
using ArtisansGuns.Weapons;

/// <summary>
/// One-time editor utility to create the Onyx WeaponConfig ScriptableObject.
/// Use: menu Assets → Create Onyx WeaponConfig
/// Safe to delete this script after the asset is created.
/// </summary>
public static class CreateOnyxWeapon
{
    [MenuItem("Assets/Create Onyx WeaponConfig")]
    public static void Create()
    {
        const string path = "Assets/Resources/Weapons/Onyx.asset";

        if (AssetDatabase.LoadAssetAtPath<WeaponConfig>(path) != null)
        {
            Debug.Log("[CreateOnyxWeapon] Onyx.asset already exists at " + path);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<WeaponConfig>(path);
            return;
        }

        // Ensure directory exists
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Weapons"))
            AssetDatabase.CreateFolder("Assets/Resources", "Weapons");

        var config = ScriptableObject.CreateInstance<WeaponConfig>();

        // Identity
        config.weaponId = "onyx";
        config.weaponName = "ONYX";
        config.isKnife = false;
        config.isPrimary = true;
        config.isSecondary = false;

        // Shotgun settings
        config.isShotgun = true;
        config.pelletCount = 9;
        config.shotgunSpreadAngle = 5f;

        // Fire settings — slow fire rate for a shotgun
        config.fireRate = 80f;       // ~1.33 shots/sec
        config.isAutomatic = false;  // semi-auto pump shotgun

        // Ammo / damage
        config.maxAmmo = 7;
        config.bulletRange = 30f;    // shorter range than AR
        config.damage = 150f;        // total damage per shot (divided by pellets)
        config.headshotMultiplier = 1.5f;

        // Movement
        config.speedMultiplier = 0.9f;

        // Recoil defaults (tweak in Inspector)
        config.recoilKickAmount = 2f;
        config.recoilSmoothness = 10f;
        config.movementSpreadAngle = 3f;
        config.jumpSpreadAngle = 6f;

        // Visual recoil
        config.weaponUpForce = 0.15f;
        config.weaponBackForce = 0.2f;
        config.weaponVerticalRoll = 8f;
        config.weaponHorizontalRoll = 3f;
        config.weaponRecoverySpeed = 6f;
        config.weaponMovingMultiplier = 1.5f;

        AssetDatabase.CreateAsset(config, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
        Debug.Log($"[CreateOnyxWeapon] Created Onyx.asset at {path}. Assign prefab, icons, audio, and VFX in the Inspector.");
    }
}
