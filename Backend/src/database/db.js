const { Pool } = require('pg');
require('dotenv').config();

// PostgreSQL connection pool
// Render proporciona DATABASE_URL automáticamente
const pool = new Pool(
    process.env.DATABASE_URL
        ? {
            connectionString: process.env.DATABASE_URL,
            ssl: {
                rejectUnauthorized: false // Necesario para Render
            }
        }
        : {
            host: process.env.DB_HOST || 'localhost',
            port: process.env.DB_PORT || 5432,
            database: process.env.DB_NAME || 'artisans_guns',
            user: process.env.DB_USER || 'postgres',
            password: process.env.DB_PASSWORD,
            max: 20,
            idleTimeoutMillis: 30000,
            connectionTimeoutMillis: 2000,
        }
);

// Test connection
pool.on('connect', () => {
    console.log('✅ Connected to PostgreSQL database');
});

pool.on('error', (err) => {
    console.error('❌ Unexpected error on idle client', err);
    process.exit(-1);
});

// Helper function to execute queries
const query = async (text, params) => {
    const start = Date.now();
    try {
        const res = await pool.query(text, params);
        const duration = Date.now() - start;
        console.log('Executed query', { text, duration, rows: res.rowCount });
        return res;
    } catch (error) {
        console.error('❌ Database query error:', error);
        throw error;
    }
};

// Create tables if they don't exist
const initDatabase = async () => {
    const createUsersTable = `
        CREATE TABLE IF NOT EXISTS users (
            id SERIAL PRIMARY KEY,
            username VARCHAR(50) UNIQUE NOT NULL,
            password_hash VARCHAR(255) NOT NULL,
            character_name VARCHAR(50) NOT NULL,
            
            -- Player Loadout (configuration)
            selected_character VARCHAR(50) DEFAULT 'CRIMSON',
            level INTEGER DEFAULT 1,
            
            -- Weapon Loadout (stored as JSON)
            primary_weapon JSONB DEFAULT '{"weaponId": "talon_ar", "skinId": "default"}',
            secondary_weapon JSONB DEFAULT '{"weaponId": "bolt", "skinId": "default"}',
            knife_skin JSONB DEFAULT '{"weaponId": "knife", "skinId": "default"}',
            
            -- Unlocked Content (stored as JSON arrays)
            unlocked_characters JSONB DEFAULT '["CRIMSON"]',
            unlocked_weapon_skins JSONB DEFAULT '{"talon_ar": ["default"], "bolt": ["default"], "onyx": ["default"], "titan": ["default"], "rifle_phantom": ["default"], "pistol_ghost": ["default"]}',
            
            -- Currency
            blue_points INTEGER DEFAULT 0,
            rival_coins INTEGER DEFAULT 0,
            
            -- Hats
            selected_hat VARCHAR(50) DEFAULT 'none',
            unlocked_hats JSONB DEFAULT '["none"]',
            
            -- Progression
            xp INTEGER DEFAULT 0,
            
            -- Player Settings
            sensitivity FLOAT DEFAULT 6.0,
            
            -- Ability Loadout
            ability1 VARCHAR(50) DEFAULT 'smoke_grenade',
            ability2 VARCHAR(50) DEFAULT 'dash',
            ultimate VARCHAR(50) DEFAULT 'crimson_ultimate',
            
            -- Account Management
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            last_login TIMESTAMP,
            is_active BOOLEAN DEFAULT TRUE
        );
        
        CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
        CREATE INDEX IF NOT EXISTS idx_users_character_name ON users(character_name);
    `;

    try {
        await query(createUsersTable);
        console.log('✅ Database tables initialized');
        
        // Migrate existing users (add new columns if they don't exist)
        await migrateExistingUsers();
        
        // Create index on selected_character AFTER migration
        await query('CREATE INDEX IF NOT EXISTS idx_users_selected_character ON users(selected_character)');
        console.log('✅ Database indexes created');
        
    } catch (error) {
        console.error('❌ Failed to initialize database:', error);
        throw error;
    }
};

// Migrate existing users to add new loadout columns
const migrateExistingUsers = async () => {
    try {
        console.log('🔄 Running database migrations...');
        
        // Run each ALTER separately so one failure doesn't block others
        const columns = [
            "ADD COLUMN IF NOT EXISTS selected_character VARCHAR(50) DEFAULT 'CRIMSON'",
            "ADD COLUMN IF NOT EXISTS level INTEGER DEFAULT 1",
            `ADD COLUMN IF NOT EXISTS primary_weapon JSONB DEFAULT '{"weaponId": "talon_ar", "skinId": "default"}'`,
            `ADD COLUMN IF NOT EXISTS secondary_weapon JSONB DEFAULT '{"weaponId": "bolt", "skinId": "default"}'`,
            `ADD COLUMN IF NOT EXISTS knife_skin JSONB DEFAULT '{"weaponId": "knife", "skinId": "default"}'`,
            `ADD COLUMN IF NOT EXISTS unlocked_characters JSONB DEFAULT '["CRIMSON"]'`,
            `ADD COLUMN IF NOT EXISTS unlocked_weapon_skins JSONB DEFAULT '{"talon_ar": ["default"], "bolt": ["default"], "onyx": ["default"], "titan": ["default"], "rifle_phantom": ["default"], "pistol_ghost": ["default"]}'`,
            "ADD COLUMN IF NOT EXISTS blue_points INTEGER DEFAULT 0",
            "ADD COLUMN IF NOT EXISTS rival_coins INTEGER DEFAULT 0",
            "ADD COLUMN IF NOT EXISTS xp INTEGER DEFAULT 0",
            "ADD COLUMN IF NOT EXISTS sensitivity FLOAT DEFAULT 2.0",
            "ADD COLUMN IF NOT EXISTS is_guest BOOLEAN DEFAULT FALSE",
            "ADD COLUMN IF NOT EXISTS guest_uuid VARCHAR(64) UNIQUE",
            "ADD COLUMN IF NOT EXISTS google_id VARCHAR(128) UNIQUE",
            "ADD COLUMN IF NOT EXISTS selected_hat VARCHAR(50) DEFAULT 'none'",
            `ADD COLUMN IF NOT EXISTS unlocked_hats JSONB DEFAULT '["none"]'`,
            "ADD COLUMN IF NOT EXISTS ability1 VARCHAR(50) DEFAULT 'smoke_grenade'",
            "ADD COLUMN IF NOT EXISTS ability2 VARCHAR(50) DEFAULT 'dash'",
            "ADD COLUMN IF NOT EXISTS ultimate VARCHAR(50) DEFAULT 'crimson_ultimate'"
        ];

        for (const col of columns) {
            try {
                await query(`ALTER TABLE users ${col}`);
            } catch (colErr) {
                console.warn(`⚠️ Migration column failed: ${col} — ${colErr.message}`);
            }
        }
        
        console.log('✅ Migration columns check completed');
        
        // Update existing users with NULL loadout data (always run this to fix old users)
        const result = await query(`
            UPDATE users 
            SET 
                selected_character = COALESCE(selected_character, 'CRIMSON'),
                level = COALESCE(level, 1),
                primary_weapon = COALESCE(primary_weapon, '{"weaponId": "talon_ar", "skinId": "default"}'::jsonb),
                secondary_weapon = COALESCE(secondary_weapon, '{"weaponId": "bolt", "skinId": "default"}'::jsonb),
                unlocked_characters = COALESCE(unlocked_characters, '["CRIMSON"]'::jsonb),
                unlocked_weapon_skins = COALESCE(unlocked_weapon_skins, '{"talon_ar": ["default"], "bolt": ["default"], "onyx": ["default"], "titan": ["default"], "rifle_phantom": ["default"], "rifle_vandal": ["default"], "smg_stinger": ["default"], "pistol_ghost": ["default"], "pistol_sheriff": ["default"]}'::jsonb),
                blue_points = COALESCE(blue_points, 0),
                rival_coins = COALESCE(rival_coins, 0),
                xp = COALESCE(xp, 0)
            WHERE selected_character IS NULL OR level IS NULL OR primary_weapon IS NULL OR blue_points IS NULL OR rival_coins IS NULL OR xp IS NULL
        `);
        
        if (result.rowCount > 0) {
            console.log(`✅ Updated ${result.rowCount} users with default loadout data`);
        } else {
            console.log('✅ All users already have valid loadout data');
        }
        
    } catch (error) {
        console.error('⚠️ Migration error:', error.message);
    }
};

module.exports = {
    query,
    pool,
    initDatabase
};
