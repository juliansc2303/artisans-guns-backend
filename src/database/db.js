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
    } catch (error) {
        console.error('❌ Failed to initialize database:', error);
        throw error;
    }
};

module.exports = {
    query,
    pool,
    initDatabase
};
