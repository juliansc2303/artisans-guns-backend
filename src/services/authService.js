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
                unlocked_weapon_skins
            ) 
             VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9) 
             RETURNING id, username, character_name, selected_character, level`,
            [
                username.toLowerCase(), 
                passwordHash, 
                characterName,
                'CRIMSON', // Default character
                1, // Starting level
                JSON.stringify({ weaponId: 'rifle_phantom', skinId: 'default' }),
                JSON.stringify({ weaponId: 'pistol_ghost', skinId: 'default' }),
                JSON.stringify(['CRIMSON']), // Starting with CRIMSON unlocked
                JSON.stringify({
                    rifle_phantom: ['default'],
                    rifle_vandal: ['default'],
                    smg_stinger: ['default'],
                    pistol_ghost: ['default'],
                    pistol_sheriff: ['default']
                })
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
                unlocked_characters,
                unlocked_weapon_skins
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
                unlockedCharacters: user.unlocked_characters,
                unlockedWeaponSkins: user.unlocked_weapon_skins
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

module.exports = {
    register,
    login,
    verifyToken
};
