/**
 * Reset script - Deletes ALL user accounts from the database
 * Run with: node reset-users.js
 */
require('dotenv').config();
const { Pool } = require('pg');

const pool = new Pool({
    host: process.env.DB_HOST || 'localhost',
    port: process.env.DB_PORT || 5432,
    database: process.env.DB_NAME || 'artisans_guns',
    user: process.env.DB_USER || 'postgres',
    password: process.env.DB_PASSWORD,
});

(async () => {
    try {
        // Show current users
        const before = await pool.query('SELECT id, username, character_name, is_guest, blue_points, rival_coins, unlocked_weapon_skins FROM users');
        console.log(`\n📋 Found ${before.rows.length} users:`);
        before.rows.forEach(u => {
            console.log(`  - ID:${u.id} | ${u.username} (${u.character_name}) | Guest:${u.is_guest} | BP:${u.blue_points} RC:${u.rival_coins}`);
            console.log(`    Skins: ${JSON.stringify(u.unlocked_weapon_skins)}`);
        });

        // Delete all users
        const result = await pool.query('DELETE FROM users');
        console.log(`\n🗑️  Deleted ${result.rowCount} users.`);
        console.log('✅ Database reset complete. All accounts removed.\n');
    } catch (err) {
        console.error('❌ Error:', err.message);
    } finally {
        await pool.end();
    }
})();
