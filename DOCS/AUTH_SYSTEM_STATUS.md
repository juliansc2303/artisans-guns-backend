# 🎮 Artisans Guns - Sistema de Autenticación
## Estado Actual y Roadmap

**Fecha:** Febrero 2, 2026  
**Versión:** MVP v1.0  
**Estado:** ✅ Backend desplegado en producción, UI implementada, listo para testing

---

## 📋 RESUMEN EJECUTIVO

Sistema de autenticación tradicional (username/password) diseñado para público infantil/adolescente que no requiere email. Backend profesional con PostgreSQL + JWT desplegado en Render.com con HTTPS gratuito. UI elite cyberpunk implementada con UI Toolkit.

---

## 🏗️ ARQUITECTURA ACTUAL

### Backend Stack
- **Framework:** Node.js + Express
- **Base de datos:** PostgreSQL (Render.com free tier)
- **Autenticación:** JWT (JSON Web Tokens)
- **Seguridad:** 
  - bcrypt (10 rounds) para passwords
  - helmet para headers HTTP
  - CORS dinámico (dev: *, prod: whitelist)
  - XOR encryption + Base64 para tokens en PlayerPrefs
- **Rate Limiting:** ❌ REMOVIDO (decisión de diseño para evitar frustración en MVP)

### Endpoints Disponibles

#### 1. POST `/api/auth/register`
**Request:**
```json
{
  "username": "string (3-50 chars, alphanumeric + _)",
  "password": "string (min 6 chars)",
  "characterName": "string (3-20 chars, alphanumeric + spaces)"
}
```

**Response (201):**
```json
{
  "success": true,
  "message": "User registered successfully",
  "user": {
    "userId": 123,
    "username": "player123",
    "characterName": "CRIMSON"
  },
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Validaciones:**
- Username único en DB
- Character name no puede ser solo espacios
- Regex: `^[a-zA-Z0-9_]+$` (username), `^[a-zA-Z0-9\s]+$` (character name)

#### 2. POST `/api/auth/login`
**Request:**
```json
{
  "username": "string",
  "password": "string"
}
```

**Response (200):**
```json
{
  "success": true,
  "message": "Login successful",
  "user": {
    "userId": 123,
    "username": "player123",
    "characterName": "CRIMSON"
  },
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Errores:**
- 401: Credenciales inválidas
- 404: Usuario no existe

#### 3. POST `/api/auth/verify`
**Headers:**
```
Authorization: Bearer <JWT_TOKEN>
```

**Response (200):**
```json
{
  "valid": true,
  "user": {
    "userId": 123,
    "username": "player123",
    "characterName": "CRIMSON"
  }
}
```

### JWT Configuration
- **Algoritmo:** HS256
- **Secret:** 64 caracteres (`m@#T6fJ(x8Gr*W'cr-s+y'5uUS?)?`kZ*)FsJ"9Hp;IXI.d_A%ec.B4KQlggdSN|`)
- **Expiración:** 7 días
- **Payload:**
```json
{
  "userId": 123,
  "username": "player123",
  "iat": 1234567890,
  "exp": 1234567890
}
```

### Base de Datos Schema

```sql
CREATE TABLE users (
    user_id SERIAL PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    character_name VARCHAR(20) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_username ON users(username);
CREATE INDEX idx_character_name ON users(character_name);
```

---

## 🚀 DEPLOYMENT

### Producción (Render.com)
- **URL Backend:** `https://artisans-guns-api.onrender.com`
- **PostgreSQL:** artisans-guns-db (interno)
- **Git Repo:** https://github.com/juliansc2303/artisans-guns-backend
- **Branch:** main
- **Auto-deploy:** Activado (push to main → deploy automático)
- **Cold Start:** ~30 segundos después de 15 min inactividad
- **SSL:** Automático (HTTPS incluido)

### Desarrollo Local
- **URL:** `http://localhost:3000`
- **PostgreSQL:** localhost:5432
- **Database:** artisans_guns
- **Credenciales:** Ver `Backend/.env`

### Variables de Entorno (.env)

```bash
# Database
DB_HOST=localhost
DB_PORT=5432
DB_NAME=artisans_guns
DB_USER=postgres
DB_PASSWORD=password
DATABASE_URL=postgresql://user:pass@host:5432/db  # Solo Render

# JWT
JWT_SECRET=m@#T6fJ(x8Gr*W'cr-s+y'5uUS?)?`kZ*)FsJ"9Hp;IXI.d_A%ec.B4KQlggdSN|

# Server
PORT=3000
NODE_ENV=production  # development | production

# CORS
CORS_ORIGIN=*  # En producción: lista de dominios permitidos
```

---

## 🎨 UNITY IMPLEMENTATION

### Scripts

#### 1. `AuthManager.cs` (Singleton)
**Ubicación:** `Assets/Scripts/Auth/AuthManager.cs`

**Estado actual:**
- ⚠️ **TEMPORAL:** Hardcoded a producción para testing
```csharp
private const string BASE_URL = "https://artisans-guns-api.onrender.com/api";
```

- ✅ **PENDIENTE:** Restaurar auto-switching después de testing:
```csharp
#if UNITY_EDITOR
    private const string BASE_URL = "http://localhost:3000/api";
#else
    private const string BASE_URL = "https://artisans-guns-api.onrender.com/api";
#endif
```

**Métodos principales:**
- `Register(username, password, characterName)` → POST `/register`
- `Login(username, password)` → POST `/login`
- `VerifyToken(callback)` → POST `/verify`
- `IsLoggedIn()` → Verifica token local
- `Logout()` → Limpia PlayerPrefs

**Eventos:**
```csharp
public event Action<UserData> OnLoginSuccess;
public event Action<string> OnLoginFailed;
public event Action<UserData> OnRegisterSuccess;
public event Action<string> OnRegisterFailed;
```

**Token Storage:**
- Encriptación XOR + Base64
- Guardado en `PlayerPrefs` key: `auth_token`
- Automáticamente incluido en header `Authorization: Bearer <token>`

**Timeout:**
- 60 segundos para manejar cold starts de Render

#### 2. `AuthUIController.cs`
**Ubicación:** `Assets/Scripts/UI/AuthUIController.cs`

**Paneles:**
1. **Login Panel:** Username + Password
2. **Register Panel:** Username + Password + Repeat + Character Name
3. **Success Panel:** ✓ SUCCESS + Character name display + Continue button
4. **Loading Overlay:** Aparece durante llamadas API

**Flujo de navegación:**
```
[Login Panel] ──register──> [Register Panel]
     │                            │
     │                            │ (success)
     │                            ▼
     │                    [Success Panel]
     │                            │
     │ <──────continue─────────────┘
     │
     │ (login success)
     ▼
 [Lobby Scene]
```

**Características:**
- ✅ Validación client-side antes de enviar
- ✅ Botón "Create Account" se deshabilita durante API call (previene spam)
- ✅ Panel de éxito dedicado con estética elite
- ✅ Errores permanecen en panel actual (no cambia de vista)
- ✅ Auto-verificación de sesión existente al abrir app

### UI Toolkit Files

#### `LoginScreen.uxml`
**Ubicación:** `Assets/UI/Auth/LoginScreen.uxml`

**Estructura:**
- Background con imagen
- Main container centrado
- 3 paneles (login, register, success)
- Loading overlay

**Elementos clave:**
- Sin iconos en inputs (diseño limpio)
- Success panel con checkmark "✓ SUCCESS"
- Character name display destacado

#### `LoginScreen.uss`
**Ubicación:** `Assets/UI/Auth/LoginScreen.uss`

**Diseño:** Cyberpunk Elite Glassmorphism
- **Colores base:**
  - Background: `rgb(8, 10, 16)`
  - Panel: `rgba(18, 22, 35, 0.75)`
  - Borders: `rgba(100, 180, 255)` (azul neón)
  - Success: `rgba(100, 255, 180)` (verde neón)
  
- **Efectos:**
  - Text shadows para glow neón
  - Gradientes en bordes (top/left más brillantes)
  - Input accent border (3px izquierdo que se ilumina en hover)
  - Botones con efecto 3D (border top/bottom diferentes)

- **Responsive:**
  - Width: 86% (min: 360px, max: 460px)
  - Padding: 60px vertical, 55px horizontal
  - Input height: 64px
  - Button height: 64px (primary), 52px (secondary)

- **Estados:**
  - `.status-error` → Texto rojo con glow
  - `.success-glow` → Texto verde con glow intenso
  - `.hidden` → `display: none`

---

## ✅ COMPLETADO

### Backend
- [x] Node.js + Express setup
- [x] PostgreSQL connection (local + Render)
- [x] JWT authentication implementation
- [x] bcrypt password hashing
- [x] Input validation (express-validator)
- [x] Error handling middleware
- [x] CORS configuration
- [x] Helmet security headers
- [x] Database schema + indexes
- [x] GitHub repository setup
- [x] Render.com deployment
- [x] HTTPS automatic certificate
- [x] Rate limiting REMOVED (diseño para MVP)

### Unity
- [x] AuthManager singleton con eventos
- [x] Token encryption (XOR + Base64)
- [x] UnityWebRequest implementation
- [x] Auto token verification on startup
- [x] PlayerPrefs token persistence
- [x] UI Toolkit migration (UGUI → UI Toolkit)
- [x] AuthUIController con navegación de paneles
- [x] Cyberpunk elite glassmorphism design
- [x] Panel de éxito dedicado
- [x] Validaciones client-side
- [x] Loading overlay con timeout 60s
- [x] Button spam prevention
- [x] Error states con visual feedback
- [x] Success states con panel dedicado

---

## 🔜 PRÓXIMOS PASOS

### Inmediato (Testing Phase)
1. **Restaurar auto-switching en AuthManager.cs:**
   - Descomentar `#if UNITY_EDITOR` conditional compilation
   - Remover hardcoded production URL
   - Testing en Editor (localhost) y Build (Render)

2. **Testing completo:**
   - Registro con credenciales válidas
   - Login con credenciales correctas/incorrectas
   - Verificación de token al reabrir app
   - Flujo completo: Register → Success → Login → Lobby

3. **Mobile build inicial:**
   - Android build con backend de producción
   - Testing en dispositivo real
   - Verificar UI responsive en diferentes resoluciones

### Networking (Siguiente fase)
**DECISIÓN:** Photon Fusion 2 (Shared Mode + Host Authority)

**Razones:**
- ✅ 100 CCU gratis (suficiente para MVP + lanzamiento)
- ✅ 0 configuración de red (funciona con NAT)
- ✅ Photon Cloud incluido (matchmaking automático)
- ✅ Host Authority perfecto para IA de bots
- ✅ Testing instantáneo sin servidor dedicado
- ✅ Ideal para jugar con sobrinos/testing MVP
- ❌ Fishnet descartado (requiere IP pública + puerto abierto)

**Arquitectura planeada:**
```
Auth Backend (Node.js + PostgreSQL)
      ↓ login/register
Unity Client
      ↓ connect with userID
Photon Fusion Cloud (Shared Mode)
      ├─ Host: Controla IA + física
      └─ Clients: Sincronización
```

**Plan de integración:**
1. Instalar Photon Fusion SDK
2. Configurar AppID en Photon Dashboard
3. NetworkRunner setup (Shared Mode)
4. NetworkObject para personajes
5. RPC para acciones (disparo, daño)
6. Host Authority para IA enemigos
7. Matchmaking básico (Create/Join Room)
8. Integración con AuthManager (userId en Custom Properties)

### Features futuras (Post-MVP)
- [ ] Recuperación de contraseña (requiere email)
- [ ] Sistema de amigos
- [ ] Leaderboards
- [ ] Stats persistentes (kills, deaths, wins)
- [ ] Inventario/Progresión
- [ ] In-app purchases (skins, armas)
- [ ] Host Migration (Photon Fusion)
- [ ] Migración a Server Mode (si crece > 100 CCU)

---

## 🛠️ TROUBLESHOOTING

### Errores comunes

**1. "Too many login attempts"**
- ❌ YA NO APLICA - Rate limiting removido

**2. "Request timeout"**
- Render cold start (~30 seg)
- Timeout configurado a 60s
- Normal en primera request después de inactividad

**3. "Invalid token"**
- Token expirado (7 días)
- Usuario debe hacer login nuevamente
- AuthManager.VerifyToken() maneja automáticamente

**4. "Username already exists"**
- Validación de unicidad en DB
- Mensaje claro al usuario
- Sugerir nombre alternativo

**5. Backend no responde en localhost**
- Verificar que Node.js esté corriendo: `npm run dev`
- Puerto 3000 libre
- PostgreSQL corriendo en localhost:5432

### Testing recomendado

**Casos de prueba:**
1. ✅ Registro exitoso → Panel de éxito → Login
2. ✅ Registro con username duplicado → Error en panel register
3. ✅ Login con credenciales incorrectas → Error en panel login
4. ✅ Token válido → Auto-login directo a lobby
5. ✅ Token expirado → Forzar nuevo login
6. ✅ Cold start de Render → Espera de 30s sin timeout
7. ✅ Múltiples clicks en "Create Account" → Botón deshabilitado

---

## 📊 MÉTRICAS Y LÍMITES

### Render.com Free Tier
- **750 horas/mes** de uptime (suficiente para MVP)
- **Sleep después de 15 min** inactividad
- **PostgreSQL:** 90 días de retención de backups
- **Bandwidth:** Ilimitado
- **RAM:** 512 MB
- **Cold start:** ~30 segundos

### Photon Fusion Free Tier
- **100 CCU** simultáneos
- **Bandwidth:** Ilimitado
- **Regiones:** Todas disponibles
- **Matchmaking:** Incluido

### PostgreSQL
- **Max connections:** 10 (Render free tier)
- **Storage:** 1 GB (expandible)
- **Indexes:** username, character_name

---

## 🔐 SEGURIDAD

### Implementado
- ✅ Password hashing con bcrypt (10 rounds)
- ✅ JWT con secret de 64 caracteres
- ✅ Token expiration (7 días)
- ✅ CORS configurado por entorno
- ✅ Helmet headers (XSS, CSRF protection)
- ✅ Input validation server-side
- ✅ SQL injection prevention (parameterized queries)
- ✅ Token encryption en PlayerPrefs (XOR + Base64)

### Pendiente (Post-MVP)
- [ ] Rate limiting (removido temporalmente para MVP)
- [ ] IP banning para abuso extremo
- [ ] 2FA (opcional para cuentas premium)
- [ ] Email verification (si se agrega email)
- [ ] Password strength meter
- [ ] CAPTCHA para registro (si hay bot spam)

---

## 📝 NOTAS IMPORTANTES

### Decisiones de diseño

1. **Sin email requerido:**
   - Target: Niños/adolescentes que pueden no tener email
   - Simplifica onboarding
   - Trade-off: No recuperación de contraseña (aceptable para MVP)

2. **Sin rate limiting:**
   - Evita frustración en testing MVP
   - Target: Público infantil que puede reintentar muchas veces
   - Riesgo de brute force aceptable para fase MVP
   - Se reactivará en producción con usuarios reales

3. **JWT de 7 días:**
   - Balance entre seguridad y UX
   - Evita re-login frecuente en móviles
   - Aceptable para juego casual

4. **Photon Fusion (Shared Mode):**
   - Decisión técnica por limitación de NAT
   - 100 CCU gratis suficiente para lanzamiento
   - Permite testing real sin infraestructura

### Contexto del proyecto
- **Juego:** Hero FPS móvil con personajes aves
- **Plataforma:** Android/iOS (inicio con Android)
- **Distribución:** APK directo (fuera de Play Store inicialmente)
- **Target:** Público infantil/adolescente
- **Filosofía:** Calidad de producción desde MVP, sin refactoring futuro

---

## 📚 RECURSOS

### Repositorios
- **Backend:** https://github.com/juliansc2303/artisans-guns-backend
- **Unity:** (local, sin repo público aún)

### Documentación externa
- [Photon Fusion Docs](https://doc.photonengine.com/fusion)
- [Express.js Security Best Practices](https://expressjs.com/en/advanced/best-practice-security.html)
- [Unity UI Toolkit Manual](https://docs.unity3d.com/Manual/UIElements.html)

### Contacto
- Email: threeformy@gmail.com
- GitHub: juliansc2303

---

**Última actualización:** Febrero 2, 2026  
**Próxima revisión:** Después de testing completo + mobile build
