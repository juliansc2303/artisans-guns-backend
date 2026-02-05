const express = require('express');
const cors = require('cors');
const helmet = require('helmet');
require('dotenv').config();

const { initDatabase } = require('./database/db');
const authRoutes = require('./routes/authRoutes');
const loadoutRoutes = require('./routes/loadoutRoutes');

const app = express();
const PORT = process.env.PORT || 3000;

// Security middleware
app.use(helmet());

// CORS - restringido para producción, abierto para desarrollo
const allowedOrigins = process.env.CORS_ORIGIN 
    ? process.env.CORS_ORIGIN.split(',') 
    : ['http://localhost:3000', 'http://127.0.0.1:3000'];

app.use(cors({
    origin: function (origin, callback) {
        // Permitir requests sin origin (apps móviles, Postman)
        if (!origin) return callback(null, true);
        
        // En desarrollo, permitir todo
        if (process.env.NODE_ENV === 'development') {
            return callback(null, true);
        }
        
        // En producción, verificar whitelist
        if (allowedOrigins.indexOf(origin) !== -1) {
            callback(null, true);
        } else {
            callback(new Error('Not allowed by CORS'));
        }
    },
    methods: ['GET', 'POST', 'PUT', 'DELETE'],
    allowedHeaders: ['Content-Type', 'Authorization'],
    credentials: true
}));

// Body parser middleware
app.use(express.json());
app.use(express.urlencoded({ extended: true }));

// Request logging middleware
app.use((req, res, next) => {
    console.log(`${req.method} ${req.path} - ${new Date().toISOString()}`);
    next();
});

// Health check endpoint
app.get('/health', (req, res) => {
    res.json({
        status: 'ok',
        timestamp: new Date().toISOString(),
        service: 'Artisans Guns Backend',
        version: '1.0.0'
    });
});

// API Routes
app.use('/api/auth', authRoutes);
app.use('/api/loadout', loadoutRoutes);

// 404 handler
app.use((req, res) => {
    res.status(404).json({
        success: false,
        error: 'Endpoint not found'
    });
});

// Error handler
app.use((err, req, res, next) => {
    console.error('❌ Server error:', err);
    res.status(500).json({
        success: false,
        error: 'Internal server error'
    });
});

// Start server
const startServer = async () => {
    try {
        // Initialize database
        await initDatabase();

        // Start listening
        app.listen(PORT, () => {
            console.log('');
            console.log('='.repeat(50));
            console.log('🚀 Artisans Guns Backend Server');
            console.log('='.repeat(50));
            console.log(`📡 Server running on port ${PORT}`);
            console.log(`🌍 Environment: ${process.env.NODE_ENV || 'development'}`);
            console.log(`🗄️  Database: ${process.env.DB_NAME || 'artisans_guns'}`);
            console.log('');
            console.log('Available endpoints:');
            console.log(`  GET  http://localhost:${PORT}/health`);
            console.log(`  POST http://localhost:${PORT}/api/auth/register`);
            console.log(`  POST http://localhost:${PORT}/api/auth/login`);
            console.log(`  POST http://localhost:${PORT}/api/auth/verify`);
            console.log(`  GET  http://localhost:${PORT}/api/loadout`);
            console.log(`  PUT  http://localhost:${PORT}/api/loadout`);
            console.log(`  GET  http://localhost:${PORT}/api/loadout/inventory`);
            console.log('='.repeat(50));
            console.log('');
        });

    } catch (error) {
        console.error('❌ Failed to start server:', error);
        process.exit(1);
    }
};

// Handle graceful shutdown
process.on('SIGTERM', () => {
    console.log('👋 SIGTERM received, shutting down gracefully...');
    process.exit(0);
});

process.on('SIGINT', () => {
    console.log('👋 SIGINT received, shutting down gracefully...');
    process.exit(0);
});

// Start the server
startServer();

module.exports = app;
