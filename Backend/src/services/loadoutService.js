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
                knife_skin,
                unlocked_characters,
                unlocked_weapon_skins,
                blue_points,
                rival_coins,
                xp,
                sensitivity,
                selected_hat,
                unlocked_hats
             FROM users 
             WHERE id = $1`,
            [userId]
        );

        if (result.rows.length === 0) {
            throw new Error('User not found');
        }

        const loadout = result.rows[0];

        // Try to fetch ability columns (may not exist if migration hasn't run)
        let abilities = { ability1: null, ability2: null, ultimate: null };
        try {
            const abilityResult = await query(
                'SELECT ability1, ability2, ultimate FROM users WHERE id = $1',
                [userId]
            );
            if (abilityResult.rows.length > 0) {
                abilities = abilityResult.rows[0];
            }
        } catch (abilityErr) {
            console.warn('[getLoadout] Ability columns not available yet:', abilityErr.message);
        }

        // Recalculate level from XP (handles formula changes gracefully)
        const xp = loadout.xp || 0;
        const correctLevel = levelFromXp(xp);
        if (correctLevel !== loadout.level) {
            console.log(`[getLoadout] Fixing level for user ${userId}: DB had ${loadout.level}, correct is ${correctLevel} (xp=${xp})`);
            await query('UPDATE users SET level = $1 WHERE id = $2', [correctLevel, userId]);
            loadout.level = correctLevel;
        }

        return {
            success: true,
            loadout: {
                selectedCharacter: loadout.selected_character,
                level: loadout.level,
                primaryWeapon: loadout.primary_weapon,
                secondaryWeapon: loadout.secondary_weapon,
                knifeSkin: loadout.knife_skin,
                unlockedCharacters: loadout.unlocked_characters,
                unlockedWeaponSkins: loadout.unlocked_weapon_skins,
                bluePoints: loadout.blue_points || 0,
                rivalCoins: loadout.rival_coins || 0,
                xp: loadout.xp || 0,
                sensitivity: loadout.sensitivity != null ? loadout.sensitivity : 6.0,
                selectedHat: loadout.selected_hat || 'none',
                unlockedHats: loadout.unlocked_hats || ['none'],
                ability1: abilities.ability1 || 'smoke_grenade',
                ability2: abilities.ability2 || 'dash',
                ultimate: abilities.ultimate || 'crimson_ultimate'
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
        const { selectedCharacter, primaryWeapon, secondaryWeapon, knifeSkin, sensitivity, selectedHat, ability1, ability2, ultimate } = loadoutData;

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
            const lowerSelected = selectedCharacter.toLowerCase();
            const hasCharacter = unlockedCharacters.some(c => c.toLowerCase() === lowerSelected);
            if (!hasCharacter) {
                throw new Error('Character not unlocked');
            }
        }

        // Validate weapon skins (if provided)
        // Note: "default" skins are always available and don't need validation
        if (primaryWeapon || secondaryWeapon || knifeSkin) {
            const userResult = await query(
                'SELECT unlocked_weapon_skins FROM users WHERE id = $1',
                [userId]
            );

            const unlockedSkins = userResult.rows[0].unlocked_weapon_skins;

            if (primaryWeapon && primaryWeapon.weaponId && primaryWeapon.skinId && primaryWeapon.skinId !== 'default') {
                const weaponSkins = unlockedSkins[primaryWeapon.weaponId] || [];
                if (!weaponSkins.includes(primaryWeapon.skinId)) {
                    throw new Error(`Primary weapon skin '${primaryWeapon.skinId}' not unlocked for ${primaryWeapon.weaponId}`);
                }
            }

            if (secondaryWeapon && secondaryWeapon.weaponId && secondaryWeapon.skinId && secondaryWeapon.skinId !== 'default') {
                const weaponSkins = unlockedSkins[secondaryWeapon.weaponId] || [];
                if (!weaponSkins.includes(secondaryWeapon.skinId)) {
                    throw new Error(`Secondary weapon skin '${secondaryWeapon.skinId}' not unlocked for ${secondaryWeapon.weaponId}`);
                }
            }

            if (knifeSkin && knifeSkin.weaponId && knifeSkin.skinId && knifeSkin.skinId !== 'default') {
                const knifeSkins = unlockedSkins['knife'] || [];
                if (!knifeSkins.includes(knifeSkin.skinId)) {
                    throw new Error(`Knife skin '${knifeSkin.skinId}' not unlocked`);
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

        if (knifeSkin) {
            updates.push(`knife_skin = $${paramIndex++}`);
            values.push(JSON.stringify(knifeSkin));
        }

        if (sensitivity !== undefined && sensitivity !== null) {
            const clampedSensitivity = Math.max(1.0, Math.min(100.0, parseFloat(sensitivity)));
            updates.push(`sensitivity = $${paramIndex++}`);
            values.push(clampedSensitivity);
        }

        if (selectedHat) {
            // Validate ownership
            const hatCheck = await query('SELECT unlocked_hats FROM users WHERE id = $1', [userId]);
            if (hatCheck.rows.length > 0) {
                const owned = hatCheck.rows[0].unlocked_hats || ['none'];
                if (selectedHat !== 'none' && !owned.includes(selectedHat)) {
                    throw new Error('Hat not unlocked');
                }
            }
            updates.push(`selected_hat = $${paramIndex++}`);
            values.push(selectedHat);
        }

        if (ability1) {
            // Only add ability columns if they exist in DB
            try {
                await query('SELECT ability1 FROM users LIMIT 0');
                updates.push(`ability1 = $${paramIndex++}`);
                values.push(ability1);
            } catch (e) {
                console.warn('[updateLoadout] ability1 column not available');
            }
        }

        if (ability2) {
            try {
                await query('SELECT ability2 FROM users LIMIT 0');
                updates.push(`ability2 = $${paramIndex++}`);
                values.push(ability2);
            } catch (e) {
                console.warn('[updateLoadout] ability2 column not available');
            }
        }

        if (ultimate) {
            try {
                await query('SELECT ultimate FROM users LIMIT 0');
                updates.push(`ultimate = $${paramIndex++}`);
                values.push(ultimate);
            } catch (e) {
                console.warn('[updateLoadout] ultimate column not available');
            }
        }

        if (updates.length === 0) {
            throw new Error('No loadout data provided');
        }

        values.push(userId); // Last parameter is userId

        const updateQuery = `
            UPDATE users 
            SET ${updates.join(', ')}
            WHERE id = $${paramIndex}
            RETURNING selected_character, primary_weapon, secondary_weapon, knife_skin, level, sensitivity,
                      unlocked_characters, unlocked_weapon_skins, blue_points, rival_coins, selected_hat, unlocked_hats
        `;

        const result = await query(updateQuery, values);

        console.log(`✅ Loadout updated for user ID ${userId}`);

        // Fetch ability columns separately (may not exist)
        let updatedAbilities = { ability1: null, ability2: null, ultimate: null };
        try {
            const abResult = await query('SELECT ability1, ability2, ultimate FROM users WHERE id = $1', [userId]);
            if (abResult.rows.length > 0) updatedAbilities = abResult.rows[0];
        } catch (e) { /* columns don't exist yet */ }

        return {
            success: true,
            loadout: {
                selectedCharacter: result.rows[0].selected_character,
                primaryWeapon: result.rows[0].primary_weapon,
                secondaryWeapon: result.rows[0].secondary_weapon,
                knifeSkin: result.rows[0].knife_skin,
                level: result.rows[0].level,
                sensitivity: result.rows[0].sensitivity != null ? result.rows[0].sensitivity : 6.0,
                unlockedCharacters: result.rows[0].unlocked_characters,
                unlockedWeaponSkins: result.rows[0].unlocked_weapon_skins,
                bluePoints: result.rows[0].blue_points || 0,
                rivalCoins: result.rows[0].rival_coins || 0,
                selectedHat: result.rows[0].selected_hat || 'none',
                unlockedHats: result.rows[0].unlocked_hats || ['none'],
                ability1: updatedAbilities.ability1 || 'smoke_grenade',
                ability2: updatedAbilities.ability2 || 'dash',
                ultimate: result.rows[0].ultimate || 'crimson_ultimate'
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
                unlocked_weapon_skins,
                blue_points,
                rival_coins,
                xp,
                level
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
                unlockedWeaponSkins: inventory.unlocked_weapon_skins,
                bluePoints: inventory.blue_points || 0,
                rivalCoins: inventory.rival_coins || 0,
                xp: inventory.xp || 0,
                level: inventory.level || 1
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

/**
 * Purchase a weapon skin - checks balance, deducts currency, unlocks skin
 * @param {number} userId
 * @param {string} weaponId - e.g. "rifle_phantom"
 * @param {string} skinId - e.g. "talon_skull"
 * @param {number} price - cost of the skin
 * @param {string} currencyType - "blue_points" or "rival_coins"
 */
const purchaseSkin = async (userId, weaponId, skinId, price, currencyType) => {
    try {
        if (!['blue_points', 'rival_coins'].includes(currencyType)) {
            throw new Error('Invalid currency type');
        }

        if (!Number.isInteger(price) || price <= 0) {
            throw new Error('Invalid price');
        }

        // Get current balance and unlocked skins in one query
        const current = await query(
            `SELECT ${currencyType}, unlocked_weapon_skins FROM users WHERE id = $1`,
            [userId]
        );

        if (current.rows.length === 0) {
            throw new Error('User not found');
        }

        const balance = current.rows[0][currencyType] || 0;
        const unlockedSkins = current.rows[0].unlocked_weapon_skins || {};

        // Check if already owned
        const weaponSkins = unlockedSkins[weaponId] || [];
        if (weaponSkins.includes(skinId)) {
            throw new Error('Skin already owned');
        }

        // Check sufficient balance
        if (balance < price) {
            throw new Error('Insufficient funds');
        }

        // Add skin to unlocked list
        if (!unlockedSkins[weaponId]) {
            unlockedSkins[weaponId] = [];
        }
        unlockedSkins[weaponId].push(skinId);

        // Deduct currency and unlock skin atomically
        const result = await query(
            `UPDATE users 
             SET ${currencyType} = ${currencyType} - $1,
                 unlocked_weapon_skins = $2
             WHERE id = $3 AND ${currencyType} >= $1
             RETURNING ${currencyType}, unlocked_weapon_skins`,
            [price, JSON.stringify(unlockedSkins), userId]
        );

        if (result.rows.length === 0) {
            throw new Error('Purchase failed - insufficient funds');
        }

        console.log(`✅ Skin '${skinId}' purchased for weapon '${weaponId}' by user ${userId} (${price} ${currencyType})`);

        return {
            success: true,
            newBalance: result.rows[0][currencyType],
            unlockedWeaponSkins: result.rows[0].unlocked_weapon_skins
        };

    } catch (error) {
        console.error('❌ Purchase skin error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

// ═══════════════════════════════════════════════════════════
// XP / Level progression helpers
// ═══════════════════════════════════════════════════════════

/**
 * Total XP required to reach a given level (from level 1).
 * Per-level cost: N→N+1 = 30 * N * (100 + 2N)
 * Early levels are fast, high levels require real dedication.
 * Cumulative: totalXpForLevel(L) = 30 * [50*L*(L-1) + L*(L-1)*(2L-1)/3]
 */
const totalXpForLevel = (level) => {
    const L = level;
    return Math.round(30 * (50 * L * (L - 1) + L * (L - 1) * (2 * L - 1) / 3));
};

/**
 * Derive level + leftover XP from a cumulative XP total.
 */
const levelFromXp = (xp) => {
    let level = 1;
    while (totalXpForLevel(level + 1) <= xp) {
        level++;
    }
    return level;
};

/**
 * Process end-of-match results for a single player.
 * Calculates XP earned, coins earned, checks for level-ups, awards diamonds.
 *
 * @param {number} userId
 * @param {object} matchData - { kills, deaths, headshots, bestStreak, maxPlayers, actualPlayers, won, draw }
 * @returns {{ success, xpEarned, coinsEarned, diamondsEarned, newXp, newLevel, oldLevel, newBluePoints, newRivalCoins }}
 */
const processMatchEnd = async (userId, matchData) => {
    try {
        const { kills, deaths, headshots, bestStreak, maxPlayers, actualPlayers, won, draw } = matchData;

        // Validate inputs
        const k  = Math.max(0, Math.floor(Number(kills)  || 0));
        const d  = Math.max(0, Math.floor(Number(deaths) || 0));
        const hs = Math.max(0, Math.floor(Number(headshots) || 0));
        const bs = Math.max(0, Math.floor(Number(bestStreak) || 0));
        const maxP = Math.max(2, Math.floor(Number(maxPlayers) || 2));
        const actP = Math.max(2, Math.floor(Number(actualPlayers) || 2));

        // ── XP formula ──
        // Base XP from performance
        let baseXp = (k * 50) - (d * 10) + (hs * 25) + (bs * 15);
        baseXp = Math.max(0, baseXp);

        // Win/draw/loss multiplier: winner gets full, loser gets ~⅓
        let winMult = 0.5;
        if (won) winMult = 1.5;
        else if (draw) winMult = 1.0;

        // Player count multiplier: actP/maxP (min 0.2)
        const playerMult = Math.max(0.2, actP / maxP);

        const xpEarned = Math.max(0, Math.round(baseXp * winMult * playerMult));

        // ── Coins formula (matches client CalculatePlayerCoins) ──
        const coinRaw = (k * 10) * winMult * (1 + (actP - 2) * 0.1) - (d * 2);
        const coinsEarned = Math.max(0, Math.round(coinRaw));

        // ── Fetch current user state ──
        const userResult = await query(
            'SELECT level, xp, blue_points, rival_coins FROM users WHERE id = $1',
            [userId]
        );
        if (userResult.rows.length === 0) {
            throw new Error('User not found');
        }

        const user = userResult.rows[0];
        const oldXp = user.xp || 0;
        // Always recalculate level from XP (handles formula changes gracefully)
        const oldLevel = levelFromXp(oldXp);
        const newXp = oldXp + xpEarned;
        const newLevel = levelFromXp(newXp);

        // ── Diamond rewards for each level crossed ──
        // Level N → N+1 awards N diamonds
        let diamondsEarned = 0;
        for (let lvl = oldLevel; lvl < newLevel; lvl++) {
            diamondsEarned += lvl; // level 1→2 = 1 diamond, 2→3 = 2, etc.
        }

        // ── Update DB atomically ──
        const updateResult = await query(
            `UPDATE users 
             SET xp = $1,
                 level = $2,
                 blue_points = blue_points + $3,
                 rival_coins = rival_coins + $4
             WHERE id = $5
             RETURNING xp, level, blue_points, rival_coins`,
            [newXp, newLevel, coinsEarned, diamondsEarned, userId]
        );

        const updated = updateResult.rows[0];

        console.log(`✅ Match end processed for user ${userId}: +${xpEarned} XP, +${coinsEarned} coins, +${diamondsEarned} diamonds (lvl ${oldLevel}→${newLevel})`);

        return {
            success: true,
            xpEarned,
            coinsEarned,
            diamondsEarned,
            newXp: updated.xp,
            newLevel: updated.level,
            oldLevel,
            newBluePoints: updated.blue_points,
            newRivalCoins: updated.rival_coins
        };

    } catch (error) {
        console.error('❌ Process match end error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

/**
 * Purchase a hat - checks balance, deducts currency, unlocks hat
 * @param {number} userId
 * @param {string} hatId - e.g. "mad_hat"
 * @param {number} price - cost of the hat
 * @param {string} currencyType - "blue_points" or "rival_coins"
 */
const purchaseHat = async (userId, hatId, price, currencyType) => {
    try {
        if (!['blue_points', 'rival_coins'].includes(currencyType)) {
            throw new Error('Invalid currency type');
        }

        if (!Number.isInteger(price) || price <= 0) {
            throw new Error('Invalid price');
        }

        const current = await query(
            `SELECT ${currencyType}, unlocked_hats FROM users WHERE id = $1`,
            [userId]
        );

        if (current.rows.length === 0) {
            throw new Error('User not found');
        }

        const balance = current.rows[0][currencyType] || 0;
        const unlockedHats = current.rows[0].unlocked_hats || ['none'];

        if (unlockedHats.includes(hatId)) {
            throw new Error('Hat already owned');
        }

        if (balance < price) {
            throw new Error('Insufficient funds');
        }

        unlockedHats.push(hatId);

        const result = await query(
            `UPDATE users 
             SET ${currencyType} = ${currencyType} - $1,
                 unlocked_hats = $2
             WHERE id = $3 AND ${currencyType} >= $1
             RETURNING ${currencyType}, unlocked_hats`,
            [price, JSON.stringify(unlockedHats), userId]
        );

        if (result.rows.length === 0) {
            throw new Error('Purchase failed - insufficient funds');
        }

        console.log(`✅ Hat '${hatId}' purchased by user ${userId} (${price} ${currencyType})`);

        return {
            success: true,
            newBalance: result.rows[0][currencyType],
            unlockedHats: result.rows[0].unlocked_hats
        };

    } catch (error) {
        console.error('❌ Purchase hat error:', error.message);
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
    unlockWeaponSkin,
    purchaseSkin,
    purchaseHat,
    processMatchEnd,
    totalXpForLevel,
    levelFromXp
};
