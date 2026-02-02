# Artisans Guns - Backend API

Backend server para Artisans Guns con autenticación JWT, PostgreSQL y rate limiting.

## Tecnologías
- Node.js + Express
- PostgreSQL
- JWT Authentication
- bcrypt para passwords
- Rate limiting y CORS

## Instalación Local

```bash
npm install
cp .env.example .env
# Editar .env con tus credenciales
npm run dev
```

## Deploy a Render

1. Crear cuenta en [Render.com](https://render.com)
2. Crear PostgreSQL database (Free tier)
3. Crear Web Service conectado a este repo
4. Configurar variables de entorno
5. Deploy automático

## Variables de Entorno

Ver `.env.example` para la lista completa.

## Endpoints

- `POST /api/auth/register` - Crear cuenta
- `POST /api/auth/login` - Iniciar sesión
- `POST /api/auth/verify` - Verificar token

## Seguridad

- JWT tokens con expiración de 7 días
- Rate limiting: 5 intentos cada 15 min
- bcrypt con 10 rounds
- CORS configurado por NODE_ENV
- Helmet para headers seguros
