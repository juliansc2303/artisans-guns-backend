-- Add Knife Support to Artisans Guns Database
-- This script adds knife_skin column to users table and initializes with default knife

-- Step 1: Add knife_skin column to users table
ALTER TABLE users 
ADD COLUMN IF NOT EXISTS knife_skin JSONB DEFAULT '{"weaponId": "knife", "skinId": "default"}'::jsonb;

-- Step 2: Update existing users to have default knife skin
UPDATE users 
SET knife_skin = '{"weaponId": "knife", "skinId": "default"}'::jsonb
WHERE knife_skin IS NULL;

-- Step 3: Add knife to unlocked_weapon_skins for all users (default skin is always unlocked)
UPDATE users
SET unlocked_weapon_skins = jsonb_set(
    COALESCE(unlocked_weapon_skins, '{}'::jsonb),
    '{knife}',
    '["default"]'::jsonb,
    true
)
WHERE unlocked_weapon_skins IS NULL 
   OR NOT (unlocked_weapon_skins ? 'knife');

-- Verify the changes
SELECT 
    id,
    username,
    knife_skin,
    unlocked_weapon_skins->'knife' as unlocked_knife_skins
FROM users
LIMIT 5;

COMMIT;
