const { query } = require('../database/db');

/**
 * Get user's complete loadout/inventory
 */
const getLoadout = async (userId) => {
    try {
        const result = await query(
            `SELECT 
                selected_character,
                level,
                primary_weapon,
                secondary_weapon,
                unlocked_characters,
                unlocked_weapon_skins
             FROM users 
             WHERE id = $1`,
            [userId]
        );

        if (result.rows.length === 0) {
            throw new Error('User not found');
        }

        const loadout = result.rows[0];

        return {
            success: true,
            loadout: {
                selectedCharacter: loadout.selected_character,
                level: loadout.level,
                primaryWeapon: loadout.primary_weapon,
                secondaryWeapon: loadout.secondary_weapon,
                unlockedCharacters: loadout.unlocked_characters,
                unlockedWeaponSkins: loadout.unlocked_weapon_skins
            }
        };

    } catch (error) {
        console.error('❌ Get loadout error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

/**
 * Update user's loadout (character selection + weapons)
 */
const updateLoadout = async (userId, loadoutData) => {
    try {
        const { selectedCharacter, primaryWeapon, secondaryWeapon } = loadoutData;

        // Validate that user has this character unlocked
        if (selectedCharacter) {
            const userResult = await query(
                'SELECT unlocked_characters FROM users WHERE id = $1',
                [userId]
            );

            if (userResult.rows.length === 0) {
                throw new Error('User not found');
            }

            const unlockedCharacters = userResult.rows[0].unlocked_characters;
            if (!unlockedCharacters.includes(selectedCharacter)) {
                throw new Error('Character not unlocked');
            }
        }

        // Validate weapon skins (if provided)
        if (primaryWeapon || secondaryWeapon) {
            const userResult = await query(
                'SELECT unlocked_weapon_skins FROM users WHERE id = $1',
                [userId]
            );

            const unlockedSkins = userResult.rows[0].unlocked_weapon_skins;

            if (primaryWeapon) {
                const weaponSkins = unlockedSkins[primaryWeapon.weaponId] || [];
                if (!weaponSkins.includes(primaryWeapon.skinId)) {
                    throw new Error(`Primary weapon skin '${primaryWeapon.skinId}' not unlocked for ${primaryWeapon.weaponId}`);
                }
            }

            if (secondaryWeapon) {
                const weaponSkins = unlockedSkins[secondaryWeapon.weaponId] || [];
                if (!weaponSkins.includes(secondaryWeapon.skinId)) {
                    throw new Error(`Secondary weapon skin '${secondaryWeapon.skinId}' not unlocked for ${secondaryWeapon.weaponId}`);
                }
            }
        }

        // Build dynamic update query
        const updates = [];
        const values = [];
        let paramIndex = 1;

        if (selectedCharacter) {
            updates.push(`selected_character = $${paramIndex++}`);
            values.push(selectedCharacter);
        }

        if (primaryWeapon) {
            updates.push(`primary_weapon = $${paramIndex++}`);
            values.push(JSON.stringify(primaryWeapon));
        }

        if (secondaryWeapon) {
            updates.push(`secondary_weapon = $${paramIndex++}`);
            values.push(JSON.stringify(secondaryWeapon));
        }

        if (updates.length === 0) {
            throw new Error('No loadout data provided');
        }

        values.push(userId); // Last parameter is userId

        const updateQuery = `
            UPDATE users 
            SET ${updates.join(', ')}
            WHERE id = $${paramIndex}
            RETURNING selected_character, primary_weapon, secondary_weapon, level
        `;

        const result = await query(updateQuery, values);

        console.log(`✅ Loadout updated for user ID ${userId}`);

        return {
            success: true,
            loadout: {
                selectedCharacter: result.rows[0].selected_character,
                primaryWeapon: result.rows[0].primary_weapon,
                secondaryWeapon: result.rows[0].secondary_weapon,
                level: result.rows[0].level
            }
        };

    } catch (error) {
        console.error('❌ Update loadout error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

/**
 * Get user's inventory (unlocked content only)
 */
const getInventory = async (userId) => {
    try {
        const result = await query(
            `SELECT 
                unlocked_characters,
                unlocked_weapon_skins
             FROM users 
             WHERE id = $1`,
            [userId]
        );

        if (result.rows.length === 0) {
            throw new Error('User not found');
        }

        const inventory = result.rows[0];

        return {
            success: true,
            inventory: {
                unlockedCharacters: inventory.unlocked_characters,
                unlockedWeaponSkins: inventory.unlocked_weapon_skins
            }
        };

    } catch (error) {
        console.error('❌ Get inventory error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

/**
 * Unlock new character for user
 * (Future: Can be called when player purchases or earns character)
 */
const unlockCharacter = async (userId, characterId) => {
    try {
        const result = await query(
            `UPDATE users 
             SET unlocked_characters = 
                CASE 
                    WHEN unlocked_characters @> $1::jsonb 
                    THEN unlocked_characters
                    ELSE unlocked_characters || $1::jsonb
                END
             WHERE id = $2
             RETURNING unlocked_characters`,
            [JSON.stringify([characterId]), userId]
        );

        console.log(`✅ Character '${characterId}' unlocked for user ID ${userId}`);

        return {
            success: true,
            unlockedCharacters: result.rows[0].unlocked_characters
        };

    } catch (error) {
        console.error('❌ Unlock character error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

/**
 * Unlock weapon skin for user
 * (Future: Can be called when player purchases or earns skin)
 */
const unlockWeaponSkin = async (userId, weaponId, skinId) => {
    try {
        // Get current unlocked skins
        const current = await query(
            'SELECT unlocked_weapon_skins FROM users WHERE id = $1',
            [userId]
        );

        if (current.rows.length === 0) {
            throw new Error('User not found');
        }

        let unlockedSkins = current.rows[0].unlocked_weapon_skins;

        // Add skin to weapon's array
        if (!unlockedSkins[weaponId]) {
            unlockedSkins[weaponId] = [];
        }

        if (!unlockedSkins[weaponId].includes(skinId)) {
            unlockedSkins[weaponId].push(skinId);
        }

        // Update database
        const result = await query(
            `UPDATE users 
             SET unlocked_weapon_skins = $1
             WHERE id = $2
             RETURNING unlocked_weapon_skins`,
            [JSON.stringify(unlockedSkins), userId]
        );

        console.log(`✅ Skin '${skinId}' for weapon '${weaponId}' unlocked for user ID ${userId}`);

        return {
            success: true,
            unlockedWeaponSkins: result.rows[0].unlocked_weapon_skins
        };

    } catch (error) {
        console.error('❌ Unlock weapon skin error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

module.exports = {
    getLoadout,
    updateLoadout,
    getInventory,
    unlockCharacter,
    unlockWeaponSkin
};
