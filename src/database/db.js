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
            primary_weapon JSONB DEFAULT '{"weaponId": "rifle_phantom", "skinId": "default"}',
            secondary_weapon JSONB DEFAULT '{"weaponId": "pistol_ghost", "skinId": "default"}',
            
            -- Unlocked Content (stored as JSON arrays)
            unlocked_characters JSONB DEFAULT '["CRIMSON"]',
            unlocked_weapon_skins JSONB DEFAULT '{"rifle_phantom": ["default"], "pistol_ghost": ["default"]}',
            
            -- Account Management
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            last_login TIMESTAMP,
            is_active BOOLEAN DEFAULT TRUE
        );
        
        CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
        CREATE INDEX IF NOT EXISTS idx_users_character_name ON users(character_name);
        CREATE INDEX IF NOT EXISTS idx_users_selected_character ON users(selected_character);
    `;

    try {
        await query(createUsersTable);
        console.log('✅ Database tables initialized');
        
        // Migrate existing users (add new columns if they don't exist)
        await migrateExistingUsers();
        
    } catch (error) {
        console.error('❌ Failed to initialize database:', error);
        throw error;
    }
};

// Migrate existing users to add new loadout columns
const migrateExistingUsers = async () => {
    try {
        // Check if selected_character column exists
        const checkColumn = await query(`
            SELECT column_name 
            FROM information_schema.columns 
            WHERE table_name='users' AND column_name='selected_character'
        `);
        
        if (checkColumn.rows.length === 0) {
            console.log('🔄 Migrating existing users to new loadout schema...');
            
            await query(`
                ALTER TABLE users 
                ADD COLUMN IF NOT EXISTS selected_character VARCHAR(50) DEFAULT 'CRIMSON',
                ADD COLUMN IF NOT EXISTS level INTEGER DEFAULT 1,
                ADD COLUMN IF NOT EXISTS primary_weapon JSONB DEFAULT '{"weaponId": "rifle_phantom", "skinId": "default"}',
                ADD COLUMN IF NOT EXISTS secondary_weapon JSONB DEFAULT '{"weaponId": "pistol_ghost", "skinId": "default"}',
                ADD COLUMN IF NOT EXISTS unlocked_characters JSONB DEFAULT '["CRIMSON"]',
                ADD COLUMN IF NOT EXISTS unlocked_weapon_skins JSONB DEFAULT '{"rifle_phantom": ["default"], "pistol_ghost": ["default"]}'
            `);
            
            console.log('✅ Columns added successfully');
        }
        
        // Update existing users with NULL loadout data (always run this to fix old users)
        const result = await query(`
            UPDATE users 
            SET 
                selected_character = COALESCE(selected_character, 'CRIMSON'),
                level = COALESCE(level, 1),
                primary_weapon = COALESCE(primary_weapon, '{"weaponId": "rifle_phantom", "skinId": "default"}'::jsonb),
                secondary_weapon = COALESCE(secondary_weapon, '{"weaponId": "pistol_ghost", "skinId": "default"}'::jsonb),
                unlocked_characters = COALESCE(unlocked_characters, '["CRIMSON"]'::jsonb),
                unlocked_weapon_skins = COALESCE(unlocked_weapon_skins, '{"rifle_phantom": ["default"], "rifle_vandal": ["default"], "smg_stinger": ["default"], "pistol_ghost": ["default"], "pistol_sheriff": ["default"]}'::jsonb)
            WHERE selected_character IS NULL OR level IS NULL OR primary_weapon IS NULL
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
