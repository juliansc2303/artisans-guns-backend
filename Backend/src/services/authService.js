const bcrypt = require('bcrypt');
const jwt = require('jsonwebtoken');
const { query } = require('../database/db');

const SALT_ROUNDS = 10;

/**
 * Register a new user
 */
const register = async (username, password, characterName) => {
    try {
        // Validate input
        if (!username || username.length < 3 || username.length > 50) {
            throw new Error('Username must be between 3 and 50 characters');
        }

        if (!password || password.length < 6) {
            throw new Error('Password must be at least 6 characters');
        }

        if (!characterName || characterName.length < 3 || characterName.length > 20) {
            throw new Error('Character name must be between 3 and 20 characters');
        }

        // Validate character name format (only letters, numbers, spaces)
        if (!/^[a-zA-Z0-9\s]+$/.test(characterName)) {
            throw new Error('Character name can only contain letters, numbers and spaces');
        }

        // Check if character name is only spaces
        if (characterName.trim().length === 0) {
            throw new Error('Character name cannot be only spaces');
        }

        // Check if username already exists
        const existingUser = await query(
            'SELECT id FROM users WHERE username = $1',
            [username.toLowerCase()]
        );

        if (existingUser.rows.length > 0) {
            throw new Error('Username already exists');
        }

        // Check if character name already exists
        const existingCharacter = await query(
            'SELECT id FROM users WHERE character_name = $1',
            [characterName]
        );

        if (existingCharacter.rows.length > 0) {
            throw new Error('Character name already taken');
        }

        // Hash password
        const passwordHash = await bcrypt.hash(password, SALT_ROUNDS);

        // Insert user with default loadout
        const result = await query(
            `INSERT INTO users (
                username, 
                password_hash, 
                character_name,
                selected_character,
                level,
                primary_weapon,
                secondary_weapon,
                unlocked_characters,
                unlocked_weapon_skins,
                blue_points,
                rival_coins
            ) 
             VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11) 
             RETURNING id, username, character_name, selected_character, level`,
            [
                username.toLowerCase(), 
                passwordHash, 
                characterName,
                'crimson', // Default character
                1, // Starting level
                JSON.stringify({ weaponId: 'talon_ar', skinId: 'default' }),
                JSON.stringify({ weaponId: 'bolt', skinId: 'default' }),
                JSON.stringify(['crimson', 'vibe', 'sight', 'pato']), // Starting characters unlocked
                JSON.stringify({
                    talon_ar: ['default'],
                    bolt: ['default'],
                    onyx: ['default'],
                    titan: ['default'],
                    rifle_phantom: ['default'],
                    rifle_vandal: ['default'],
                    smg_stinger: ['default'],
                    pistol_ghost: ['default'],
                    pistol_sheriff: ['default']
                }),
                0, // Starting blue points (testing: change to 1000)
                0  // Starting rival coins
            ]
        );

        const user = result.rows[0];

        console.log(`✅ User registered: ${username} with default loadout`);

        return {
            success: true,
            user: {
                id: user.id,
                username: user.username,
                characterName: user.character_name,
                createdAt: user.created_at
            }
        };

    } catch (error) {
        console.error('❌ Registration error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

/**
 * Login user
 */
const login = async (username, password) => {
    try {
        // Validate input
        if (!username || !password) {
            throw new Error('Username and password are required');
        }

        // Get user from database with full loadout
        const result = await query(
            `SELECT 
                id, 
                username, 
                password_hash, 
                character_name, 
                is_active,
                selected_character,
                level,
                primary_weapon,
                secondary_weapon,
                knife_skin,
                unlocked_characters,
                unlocked_weapon_skins,
                blue_points,
                rival_coins,
                sensitivity
             FROM users 
             WHERE username = $1`,
            [username.toLowerCase()]
        );

        if (result.rows.length === 0) {
            throw new Error('Invalid username or password');
        }

        const user = result.rows[0];

        // Check if user is active
        if (!user.is_active) {
            throw new Error('Account is disabled');
        }

        // Verify password
        const isValidPassword = await bcrypt.compare(password, user.password_hash);

        if (!isValidPassword) {
            throw new Error('Invalid username or password');
        }

        // Update last login
        await query(
            'UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = $1',
            [user.id]
        );

        // Generate JWT token
        const token = jwt.sign(
            {
                userId: user.id,
                username: user.username,
                characterName: user.character_name
            },
            process.env.JWT_SECRET,
            { expiresIn: process.env.JWT_EXPIRES_IN || '7d' }
        );

        console.log(`✅ User logged in: ${username}`);

        return {
            success: true,
            token,
            user: {
                id: user.id,
                username: user.username,
                characterName: user.character_name,
                selectedCharacter: user.selected_character,
                level: user.level,
                primaryWeapon: user.primary_weapon,
                secondaryWeapon: user.secondary_weapon,
                knifeSkin: user.knife_skin || { weaponId: 'knife', skinId: 'default' },
                unlockedCharacters: user.unlocked_characters,
                unlockedWeaponSkins: user.unlocked_weapon_skins,
                bluePoints: user.blue_points || 0,
                rivalCoins: user.rival_coins || 0,
                sensitivity: user.sensitivity != null ? user.sensitivity : 6.0
            }
        };

    } catch (error) {
        console.error('❌ Login error:', error.message);
        return {
            success: false,
            error: error.message
        };
    }
};

/**
 * Verify JWT token
 */
const verifyToken = (token) => {
    try {
        const decoded = jwt.verify(token, process.env.JWT_SECRET);
        return {
            success: true,
            user: decoded
        };
    } catch (error) {
        return {
            success: false,
            error: 'Invalid or expired token'
        };
    }
};

/**
 * Guest login — create or retrieve a guest user by UUID.
 * The guest gets a real DB row so progress is tracked server-side.
 */
const guestLogin = async (guestUuid) => {
    try {
        if (!guestUuid || guestUuid.length < 8) {
            throw new Error('Invalid guest UUID');
        }

        // Check if guest already exists
        let result = await query(
            `SELECT id, username, character_name, selected_character, level,
                    primary_weapon, secondary_weapon, knife_skin, unlocked_characters,
                    unlocked_weapon_skins, blue_points, rival_coins, sensitivity
             FROM users WHERE guest_uuid = $1 AND is_guest = TRUE`,
            [guestUuid]
        );

        let user;

        if (result.rows.length > 0) {
            // Existing guest — update last_login
            user = result.rows[0];
            await query('UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = $1', [user.id]);
            console.log(`✅ Guest returned: ${user.username} (id=${user.id})`);
        } else {
            // New guest — create DB row
            const shortId = guestUuid.substring(0, 4).toUpperCase();
            const guestUsername = `guest_${guestUuid}`; // unique, not displayed
            const guestCharName = `Guest_${shortId}`;

            result = await query(
                `INSERT INTO users (
                    username, password_hash, character_name,
                    selected_character, level,
                    primary_weapon, secondary_weapon, knife_skin,
                    unlocked_characters, unlocked_weapon_skins,
                    blue_points, rival_coins, sensitivity,
                    is_guest, guest_uuid, last_login
                ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, TRUE, $14, CURRENT_TIMESTAMP)
                RETURNING id, username, character_name, selected_character, level,
                          primary_weapon, secondary_weapon, knife_skin, unlocked_characters,
                          unlocked_weapon_skins, blue_points, rival_coins, sensitivity`,
                [
                    guestUsername,
                    'GUEST_NO_PASSWORD', // placeholder, cannot be used for login
                    guestCharName,
                    'crimson', 1,
                    JSON.stringify({ weaponId: 'talon_ar', skinId: 'default' }),
                    JSON.stringify({ weaponId: 'bolt', skinId: 'default' }),
                    JSON.stringify({ weaponId: 'knife', skinId: 'default' }),
                    JSON.stringify(['crimson', 'vibe', 'sight', 'pato']),
                    JSON.stringify({
                        talon_ar: ['default'], bolt: ['default'],
                        rifle_phantom: ['default'], rifle_vandal: ['default'],
                        smg_stinger: ['default'], pistol_ghost: ['default'],
                        knife: ['default']
                    }),
                    0, 0, 6.0, guestUuid
                ]
            );
            user = result.rows[0];
            console.log(`✅ Guest created: ${user.character_name} (id=${user.id})`);
        }

        // Generate JWT for guest (same as normal users)
        const token = jwt.sign(
            { userId: user.id, username: user.username, characterName: user.character_name, isGuest: true },
            process.env.JWT_SECRET,
            { expiresIn: '30d' } // longer expiry for guests
        );

        return {
            success: true,
            token,
            user: {
                id: user.id,
                username: user.username,
                characterName: user.character_name,
                selectedCharacter: user.selected_character,
                level: user.level,
                primaryWeapon: user.primary_weapon,
                secondaryWeapon: user.secondary_weapon,
                knifeSkin: user.knife_skin || { weaponId: 'knife', skinId: 'default' },
                unlockedCharacters: user.unlocked_characters,
                unlockedWeaponSkins: user.unlocked_weapon_skins,
                bluePoints: user.blue_points || 0,
                rivalCoins: user.rival_coins || 0,
                sensitivity: user.sensitivity != null ? user.sensitivity : 6.0
            }
        };
    } catch (error) {
        console.error('❌ Guest login error:', error.message);
        return { success: false, error: error.message };
    }
};

/**
 * Upgrade a guest account to a full account.
 * Keeps all progress (level, loadout, currency) and sets username + password.
 */
const upgradeGuest = async (userId, newUsername, newPassword, newCharacterName) => {
    try {
        if (!newUsername || newUsername.length < 3) throw new Error('Username must be at least 3 characters');
        if (!newPassword || newPassword.length < 6) throw new Error('Password must be at least 6 characters');
        if (!newCharacterName || newCharacterName.length < 3) throw new Error('Character name must be at least 3 characters');

        // Verify user is actually a guest
        const guestCheck = await query('SELECT id, is_guest FROM users WHERE id = $1', [userId]);
        if (guestCheck.rows.length === 0) throw new Error('User not found');
        if (!guestCheck.rows[0].is_guest) throw new Error('Account is already upgraded');

        // Check username uniqueness
        const existingUser = await query('SELECT id FROM users WHERE username = $1', [newUsername.toLowerCase()]);
        if (existingUser.rows.length > 0) throw new Error('Username already exists');

        // Check character name uniqueness
        const existingChar = await query('SELECT id FROM users WHERE character_name = $1 AND id != $2', [newCharacterName, userId]);
        if (existingChar.rows.length > 0) throw new Error('Character name already taken');

        const passwordHash = await bcrypt.hash(newPassword, SALT_ROUNDS);

        const result = await query(
            `UPDATE users SET
                username = $1, password_hash = $2, character_name = $3,
                is_guest = FALSE, guest_uuid = NULL
             WHERE id = $4
             RETURNING id, username, character_name, selected_character, level,
                       primary_weapon, secondary_weapon, unlocked_characters,
                       unlocked_weapon_skins, blue_points, rival_coins, sensitivity`,
            [newUsername.toLowerCase(), passwordHash, newCharacterName, userId]
        );

        const user = result.rows[0];

        // Generate new token (no longer guest)
        const token = jwt.sign(
            { userId: user.id, username: user.username, characterName: user.character_name },
            process.env.JWT_SECRET,
            { expiresIn: process.env.JWT_EXPIRES_IN || '7d' }
        );

        console.log(`✅ Guest upgraded: id=${user.id} -> ${user.username}`);

        return {
            success: true,
            token,
            user: {
                id: user.id,
                username: user.username,
                characterName: user.character_name,
                selectedCharacter: user.selected_character,
                level: user.level,
                primaryWeapon: user.primary_weapon,
                secondaryWeapon: user.secondary_weapon,
                unlockedCharacters: user.unlocked_characters,
                unlockedWeaponSkins: user.unlocked_weapon_skins,
                bluePoints: user.blue_points || 0,
                rivalCoins: user.rival_coins || 0,
                sensitivity: user.sensitivity != null ? user.sensitivity : 6.0
            }
        };
    } catch (error) {
        console.error('❌ Upgrade guest error:', error.message);
        return { success: false, error: error.message };
    }
};

/**
 * Verify a Google ID token and extract the user's Google ID.
 * Uses Google's tokeninfo endpoint — no extra npm packages needed.
 */
const verifyGoogleIdToken = async (idToken) => {
    // Dev/Editor bypass: tokens starting with EDITOR_TEST_ are used for
    // Unity Editor testing. They map to a deterministic google_id so the
    // full flow can be tested without building to a device.
    // ONLY allowed when NODE_ENV !== 'production'.
    if (idToken && idToken.startsWith('EDITOR_TEST_') && process.env.NODE_ENV !== 'production') {
        const editorGoogleId = 'editor_' + idToken.substring(12);
        console.log(`⚙️  [DEV] Editor test token accepted -> google_id: ${editorGoogleId}`);
        return editorGoogleId;
    }

    const url = `https://oauth2.googleapis.com/tokeninfo?id_token=${encodeURIComponent(idToken)}`;
    const res = await fetch(url);
    if (!res.ok) throw new Error('Invalid Google ID token');
    const data = await res.json();
    // data.sub is the user's unique Google ID
    if (!data.sub) throw new Error('Could not extract Google user ID');
    return data.sub;
};

/**
 * Link a Google account to an existing guest user (Save Progress with Google).
 * Keeps all guest progress. Sets a character name and links the Google ID.
 */
const googleLink = async (userId, googleIdToken, characterName) => {
    try {
        if (!googleIdToken) throw new Error('Google ID token is required');
        if (!characterName || characterName.length < 3 || characterName.length > 18) {
            throw new Error('Character name must be between 3 and 18 characters');
        }
        if (!/^[a-zA-Z0-9\s]+$/.test(characterName)) {
            throw new Error('Character name can only contain letters, numbers and spaces');
        }
        if (characterName.trim().length === 0) {
            throw new Error('Character name cannot be only spaces');
        }

        // Verify the Google ID token and extract the user's Google ID
        const googleId = await verifyGoogleIdToken(googleIdToken);

        // Verify user is actually a guest
        const guestCheck = await query('SELECT id, is_guest FROM users WHERE id = $1', [userId]);
        if (guestCheck.rows.length === 0) throw new Error('User not found');
        if (!guestCheck.rows[0].is_guest) throw new Error('Account is already linked');

        // Check if this Google account is already linked to another user
        const existingGoogle = await query('SELECT id FROM users WHERE google_id = $1', [googleId]);
        if (existingGoogle.rows.length > 0) {
            throw new Error('This Google account is already linked to another player');
        }

        // Check character name uniqueness
        const existingChar = await query('SELECT id FROM users WHERE character_name = $1 AND id != $2', [characterName, userId]);
        if (existingChar.rows.length > 0) throw new Error('Character name already taken');

        // Award 1000 blue_points bonus for linking Google account
        const GOOGLE_LINK_BONUS = 1000;

        const result = await query(
            `UPDATE users SET
                character_name = $1, google_id = $2,
                is_guest = FALSE, guest_uuid = NULL,
                blue_points = blue_points + $4
             WHERE id = $3
             RETURNING id, username, character_name, selected_character, level,
                       primary_weapon, secondary_weapon, knife_skin, unlocked_characters,
                       unlocked_weapon_skins, blue_points, rival_coins, sensitivity`,
            [characterName, googleId, userId, GOOGLE_LINK_BONUS]
        );

        const user = result.rows[0];

        const token = jwt.sign(
            { userId: user.id, username: user.username, characterName: user.character_name },
            process.env.JWT_SECRET,
            { expiresIn: process.env.JWT_EXPIRES_IN || '7d' }
        );

        console.log(`✅ Guest linked to Google: id=${user.id} -> ${user.character_name} (+${GOOGLE_LINK_BONUS} blue_points bonus)`);

        return {
            success: true,
            bonusAwarded: GOOGLE_LINK_BONUS,
            token,
            user: {
                id: user.id,
                username: user.username,
                characterName: user.character_name,
                selectedCharacter: user.selected_character,
                level: user.level,
                primaryWeapon: user.primary_weapon,
                secondaryWeapon: user.secondary_weapon,
                knifeSkin: user.knife_skin || { weaponId: 'knife', skinId: 'default' },
                unlockedCharacters: user.unlocked_characters,
                unlockedWeaponSkins: user.unlocked_weapon_skins,
                bluePoints: user.blue_points || 0,
                rivalCoins: user.rival_coins || 0,
                sensitivity: user.sensitivity != null ? user.sensitivity : 6.0
            }
        };
    } catch (error) {
        console.error('❌ Google link error:', error.message);
        return { success: false, error: error.message };
    }
};

/**
 * Login with Google account. Verifies the Google ID token, then
 * finds the user by google_id.
 */
const googleLogin = async (googleIdToken) => {
    try {
        if (!googleIdToken) throw new Error('Google ID token is required');

        // Verify the Google ID token and extract the user's Google ID
        const googleId = await verifyGoogleIdToken(googleIdToken);

        const result = await query(
            `SELECT id, username, character_name, selected_character, level,
                    primary_weapon, secondary_weapon, knife_skin, unlocked_characters,
                    unlocked_weapon_skins, blue_points, rival_coins, sensitivity
             FROM users WHERE google_id = $1 AND is_active = TRUE`,
            [googleId]
        );

        if (result.rows.length === 0) {
            throw new Error('No account found with this Google account');
        }

        const user = result.rows[0];

        await query('UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = $1', [user.id]);

        const token = jwt.sign(
            { userId: user.id, username: user.username, characterName: user.character_name },
            process.env.JWT_SECRET,
            { expiresIn: process.env.JWT_EXPIRES_IN || '7d' }
        );

        console.log(`✅ Google login: ${user.character_name} (id=${user.id})`);

        return {
            success: true,
            token,
            user: {
                id: user.id,
                username: user.username,
                characterName: user.character_name,
                selectedCharacter: user.selected_character,
                level: user.level,
                primaryWeapon: user.primary_weapon,
                secondaryWeapon: user.secondary_weapon,
                knifeSkin: user.knife_skin || { weaponId: 'knife', skinId: 'default' },
                unlockedCharacters: user.unlocked_characters,
                unlockedWeaponSkins: user.unlocked_weapon_skins,
                bluePoints: user.blue_points || 0,
                rivalCoins: user.rival_coins || 0,
                sensitivity: user.sensitivity != null ? user.sensitivity : 6.0
            }
        };
    } catch (error) {
        console.error('❌ Google login error:', error.message);
        return { success: false, error: error.message };
    }
};

module.exports = {
    register,
    login,
    verifyToken,
    guestLogin,
    upgradeGuest,
    googleLink,
    googleLogin
};
