const express = require('express');
const router = express.Router();
const loadoutService = require('../services/loadoutService');
const authMiddleware = require('../middleware/authMiddleware');

/**
 * GET /api/loadout
 * Get current user's loadout
 */
router.get('/', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const result = await loadoutService.getLoadout(userId);

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Get loadout route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * PUT /api/loadout
 * Update user's loadout (character + weapons)
 * Body: {
 *   selectedCharacter?: "CRIMSON",
 *   primaryWeapon?: { weaponId: "rifle_phantom", skinId: "prime" },
 *   secondaryWeapon?: { weaponId: "pistol_ghost", skinId: "default" }
 * }
 */
router.put('/', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const loadoutData = req.body;

        const result = await loadoutService.updateLoadout(userId, loadoutData);

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Update loadout route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * GET /api/loadout/inventory
 * Get user's unlocked content (characters + weapon skins)
 */
router.get('/inventory', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const result = await loadoutService.getInventory(userId);

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Get inventory route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * POST /api/loadout/unlock-character
 * Unlock a new character for the user
 * Body: { characterId: "VIBE" }
 * 
 * NOTE: This is for future use (shop, progression, etc)
 * Currently available for testing/admin purposes
 */
router.post('/unlock-character', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const { characterId } = req.body;

        if (!characterId) {
            return res.status(400).json({
                success: false,
                error: 'characterId is required'
            });
        }

        const result = await loadoutService.unlockCharacter(userId, characterId);

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Unlock character route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * POST /api/loadout/unlock-skin
 * Unlock a weapon skin for the user
 * Body: { weaponId: "rifle_phantom", skinId: "prime" }
 * 
 * NOTE: This is for future use (shop, battle pass, etc)
 * Currently available for testing/admin purposes
 */
router.post('/unlock-skin', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const { weaponId, skinId } = req.body;

        if (!weaponId || !skinId) {
            return res.status(400).json({
                success: false,
                error: 'weaponId and skinId are required'
            });
        }

        const result = await loadoutService.unlockWeaponSkin(userId, weaponId, skinId);

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Unlock skin route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * POST /api/loadout/purchase-skin
 * Purchase a weapon skin - checks currency balance and deducts
 * Body: { weaponId: "rifle_phantom", skinId: "talon_skull", price: 500, currencyType: "blue_points" }
 */
router.post('/purchase-skin', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const { weaponId, skinId, price, currencyType } = req.body;

        if (!weaponId || !skinId || !price || !currencyType) {
            return res.status(400).json({
                success: false,
                error: 'weaponId, skinId, price, and currencyType are required'
            });
        }

        const result = await loadoutService.purchaseSkin(userId, weaponId, skinId, price, currencyType);

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Purchase skin route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * POST /api/loadout/match-end
 * Process end-of-match rewards: XP, coins, level-ups, diamonds
 * Body: { kills, deaths, headshots, bestStreak, maxPlayers, actualPlayers, won, draw }
 */
router.post('/match-end', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const { kills, deaths, headshots, bestStreak, maxPlayers, actualPlayers, won, draw } = req.body;

        if (kills == null || deaths == null) {
            return res.status(400).json({
                success: false,
                error: 'kills and deaths are required'
            });
        }

        const result = await loadoutService.processMatchEnd(userId, {
            kills, deaths, headshots, bestStreak, maxPlayers, actualPlayers, won, draw
        });

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Match end route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * GET /api/loadout/xp-curve
 * Returns XP thresholds for client to display XP bar
 * Query: ?level=5 → returns XP needed for current and next level
 */
router.get('/xp-curve', authMiddleware, async (req, res) => {
    try {
        const level = Math.max(1, Math.floor(Number(req.query.level) || 1));
        const currentLevelXp = loadoutService.totalXpForLevel(level);
        const nextLevelXp = loadoutService.totalXpForLevel(level + 1);

        res.json({
            success: true,
            level,
            currentLevelXp,
            nextLevelXp,
            xpForNextLevel: nextLevelXp - currentLevelXp
        });
    } catch (error) {
        console.error('❌ XP curve route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

/**
 * POST /api/loadout/purchase-hat
 * Purchase a hat - checks currency balance and deducts
 * Body: { hatId: "mad_hat", price: 500, currencyType: "rival_coins" }
 */
router.post('/purchase-hat', authMiddleware, async (req, res) => {
    try {
        const userId = req.user.userId;
        const { hatId, price, currencyType } = req.body;

        if (!hatId || !price || !currencyType) {
            return res.status(400).json({
                success: false,
                error: 'hatId, price, and currencyType are required'
            });
        }

        const result = await loadoutService.purchaseHat(userId, hatId, price, currencyType);

        if (!result.success) {
            return res.status(400).json({
                success: false,
                error: result.error
            });
        }

        res.json(result);

    } catch (error) {
        console.error('❌ Purchase hat route error:', error);
        res.status(500).json({
            success: false,
            error: 'Server error'
        });
    }
});

module.exports = router;
