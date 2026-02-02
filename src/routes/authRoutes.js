const express = require('express');
const { body, validationResult } = require('express-validator');
const authService = require('../services/authService');

const router = express.Router();

/**
 * POST /auth/register
 * Register a new user account
 */
router.post(
    '/register',
    [
        body('username')
            .isLength({ min: 3, max: 50 })
            .withMessage('Username must be between 3 and 50 characters')
            .matches(/^[a-zA-Z0-9_]+$/)
            .withMessage('Username can only contain letters, numbers and underscores'),
        body('password')
            .isLength({ min: 6 })
            .withMessage('Password must be at least 6 characters'),
        body('characterName')
            .isLength({ min: 3, max: 20 })
            .withMessage('Character name must be between 3 and 20 characters')
            .matches(/^[a-zA-Z0-9\s]+$/)
            .withMessage('Character name can only contain letters, numbers and spaces')
            .custom((value) => {
                if (value.trim().length === 0) {
                    throw new Error('Character name cannot be only spaces');
                }
                return true;
            })
    ],
    async (req, res) => {
        try {
            // Validate request
            const errors = validationResult(req);
            if (!errors.isEmpty()) {
                return res.status(400).json({
                    success: false,
                    error: errors.array()[0].msg
                });
            }

            const { username, password, characterName } = req.body;

            // Register user
            const result = await authService.register(username, password, characterName);

            if (!result.success) {
                return res.status(400).json(result);
            }

            res.status(201).json(result);

        } catch (error) {
            console.error('❌ Register endpoint error:', error);
            res.status(500).json({
                success: false,
                error: 'Internal server error'
            });
        }
    }
);

/**
 * POST /auth/login
 * Login with username and password
 */
router.post(
    '/login',
    [
        body('username').notEmpty().withMessage('Username is required'),
        body('password').notEmpty().withMessage('Password is required')
    ],
    async (req, res) => {
        try {
            // Validate request
            const errors = validationResult(req);
            if (!errors.isEmpty()) {
                return res.status(400).json({
                    success: false,
                    error: errors.array()[0].msg
                });
            }

            const { username, password } = req.body;

            // Login user
            const result = await authService.login(username, password);

            if (!result.success) {
                return res.status(401).json(result);
            }

            res.status(200).json(result);

        } catch (error) {
            console.error('❌ Login endpoint error:', error);
            res.status(500).json({
                success: false,
                error: 'Internal server error'
            });
        }
    }
);

/**
 * POST /auth/verify
 * Verify JWT token
 */
router.post('/verify', async (req, res) => {
    try {
        // Get token from Authorization header
        const authHeader = req.headers.authorization;
        
        if (!authHeader || !authHeader.startsWith('Bearer ')) {
            return res.status(401).json({
                valid: false,
                error: 'No token provided'
            });
        }

        const token = authHeader.substring(7); // Remove 'Bearer '

        const result = authService.verifyToken(token);

        if (!result.success) {
            return res.status(401).json({
                valid: false,
                error: result.error
            });
        }

        res.status(200).json({
            valid: true,
            user: result.user
        });

    } catch (error) {
        console.error('❌ Verify endpoint error:', error);
        res.status(500).json({
            valid: false,
            error: 'Internal server error'
        });
    }
});

module.exports = router;
