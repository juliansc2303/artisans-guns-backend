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

module.exports = router;
