-- Quick fix for julian01 user loadout data
-- Run this directly in Render's PostgreSQL console or via psql

UPDATE users 
SET 
    selected_character = COALESCE(selected_character, 'CRIMSON'),
    level = COALESCE(level, 1),
    primary_weapon = COALESCE(primary_weapon, '{"weaponId": "rifle_phantom", "skinId": "default"}'::jsonb),
    secondary_weapon = COALESCE(secondary_weapon, '{"weaponId": "pistol_ghost", "skinId": "default"}'::jsonb),
    unlocked_characters = COALESCE(unlocked_characters, '["CRIMSON"]'::jsonb),
    unlocked_weapon_skins = COALESCE(unlocked_weapon_skins, '{"rifle_phantom": ["default"], "rifle_vandal": ["default"], "smg_stinger": ["default"], "pistol_ghost": ["default"], "pistol_sheriff": ["default"]}'::jsonb)
WHERE username = 'julian01' AND (selected_character IS NULL OR level IS NULL OR primary_weapon IS NULL);

-- Verify the fix
SELECT 
    username,
    selected_character,
    level,
    primary_weapon,
    secondary_weapon,
    unlocked_characters
FROM users 
WHERE username = 'julian01';
