# Sistema de Autenticación Profesional - Guía Completa

## Stack Tecnológico

```
Unity Client (Mobile/Desktop)
    ↓ HTTPS/HTTP
Backend: Node.js + Express
    ↓
Database: PostgreSQL
```

---

## Parte 1: Setup del Backend

### Paso 1: Instalar PostgreSQL

#### Windows:
1. Descargar desde: https://www.postgresql.org/download/windows/
2. Ejecutar instalador
3. Durante instalación:
   - Puerto: `5432` (default)
   - Password: Anotar password para usuario `postgres`
   - Instalar pgAdmin 4 (herramienta visual)

#### Verificar instalación:
```powershell
psql --version
```

### Paso 2: Crear Base de Datos

```powershell
# Conectar a PostgreSQL
psql -U postgres

# Dentro de psql:
CREATE DATABASE artisans_guns;
\q
```

### Paso 3: Setup del Backend

```powershell
cd "C:\Users\julia\Artisans Guns Dos\Backend"

# Instalar dependencias
npm install

# Crear archivo .env
Copy-Item .env.example .env
```

### Paso 4: Configurar .env

Editar `Backend/.env`:

```env
PORT=3000
NODE_ENV=development

DB_HOST=localhost
DB_PORT=5432
DB_NAME=artisans_guns
DB_USER=postgres
DB_PASSWORD=TU_PASSWORD_AQUI

JWT_SECRET=cambiar_por_string_aleatorio_seguro

JWT_EXPIRES_IN=7d

CORS_ORIGIN=*
```

⚠️ **IMPORTANTE**: Cambiar `DB_PASSWORD` por tu password de PostgreSQL

### Paso 5: Iniciar Backend

```powershell
cd "C:\Users\julia\Artisans Guns Dos\Backend"

# Desarrollo (con auto-reload)
npm run dev

# O producción
npm start
```

### Paso 6: Verificar Backend

Abrir navegador: http://localhost:3000/health

Deberías ver:
```json
{
  "status": "ok",
  "timestamp": "...",
  "service": "Artisans Guns Backend",
  "version": "1.0.0"
}
```

---

## Parte 2: Setup Unity

### Paso 1: Eliminar Firebase

1. Eliminar carpetas:
   - `Assets/Firebase/`
   - `Assets/ExternalDependencyManager/`
   - `Assets/PlayServicesResolver/`
   - `Assets/google-services.json`

2. Limpiar cache:
```powershell
Remove-Item "Library\ScriptAssemblies" -Recurse -Force
Remove-Item "Temp" -Recurse -Force
```

### Paso 2: Configurar LoginScene

#### Estructura de UI:

```
Canvas
├── RegisterPanel
│   ├── Title (TextMeshPro): "Create Account"
│   ├── UsernameField (TMP_InputField)
│   │   └── Placeholder: "Username (3-50 characters)"
│   ├── PasswordField (TMP_InputField)
│   │   └── Placeholder: "Password (min 6 characters)"
│   │   └── Content Type: Password
│   ├── RepeatPasswordField (TMP_InputField)
│   │   └── Placeholder: "Repeat Password"
│   │   └── Content Type: Password
│   ├── CharacterNameField (TMP_InputField)
│   │   └── Placeholder: "Character Name (2-50 characters)"
│   ├── CreateButton (Button)
│   │   └── Text: "Create Account"
│   ├── ShowLoginButton (Button)
│   │   └── Text: "Already have an account? Login"
│   └── ErrorText (TextMeshPro)
│       └── Color: Red
│       └── Initially: Hidden
│
├── LoginPanel
│   ├── Title (TextMeshPro): "Login"
│   ├── UsernameField (TMP_InputField)
│   │   └── Placeholder: "Username"
│   ├── PasswordField (TMP_InputField)
│   │   └── Placeholder: "Password"
│   │   └── Content Type: Password
│   ├── LoginButton (Button)
│   │   └── Text: "Login"
│   ├── ShowRegisterButton (Button)
│   │   └── Text: "Create Account"
│   └── ErrorText (TextMeshPro)
│       └── Color: Red
│       └── Initially: Hidden
│
├── SuccessPanel
│   ├── SuccessIcon (Image): ✅
│   ├── MessageText (TextMeshPro): "Account created!"
│   └── GoToLoginButton (Button)
│       └── Text: "Go to Login"
│
└── LoadingPanel
    └── LoadingText (TextMeshPro): "Loading..."
```

### Paso 3: Agregar Scripts

1. **Crear GameObject AuthManager**:
   - GameObject > Create Empty → `AuthManager`
   - Add Component > `AuthManager`
   - Backend URL: `http://localhost:3000/api`
   - ⚠️ Para móvil: Cambiar `localhost` por IP de tu PC

2. **Configurar Canvas**:
   - Seleccionar Canvas
   - Add Component > `AuthUIManager`
   - Arrastrar referencias:
     - Register Panel → RegisterPanel GameObject
     - Register Username Field → campo de username
     - Register Password Field → campo de password
     - Register Repeat Password → campo de repeat
     - Register Character Name → campo de character name
     - Create Account Button → botón crear
     - Show Login From Register → botón login
     - Register Error Text → texto de error
     - Login Panel → LoginPanel GameObject
     - Login Username Field → campo username
     - Login Password Field → campo password
     - Login Button → botón login
     - Show Register Button → botón crear cuenta
     - Login Error Text → texto de error
     - Success Panel → SuccessPanel GameObject
     - Success Message Text → texto mensaje
     - Go To Login Button → botón login
     - Loading Panel → LoadingPanel GameObject
     - Lobby Scene Name: `LobbyScene`

---

## Parte 3: Testing

### Test 1: Backend Local

1. Iniciar backend: `npm run dev`
2. Verificar http://localhost:3000/health

### Test 2: Register en Unity Editor

1. Play Mode
2. Completar formulario de registro:
   - Username: `player1`
   - Password: `123456`
   - Repeat: `123456`
   - Character: `CrimsonPlayer`
3. Click "Create Account"
4. Verificar en Console: `✅ Registration successful`
5. Panel de Success debe aparecer

### Test 3: Login en Unity Editor

1. Click "Go to Login"
2. Ingresar credenciales
3. Click "Login"
4. Verificar: Carga LobbyScene

### Test 4: Verificar Base de Datos

```powershell
psql -U postgres -d artisans_guns

SELECT * FROM users;
```

Debe mostrar el usuario creado.

---

## Parte 4: Testing en Android

### Paso 1: Obtener IP de tu PC

```powershell
ipconfig
# Buscar IPv4 Address (ej: 192.168.1.100)
```

### Paso 2: Configurar Backend para Red Local

En `Backend/.env`:
```env
CORS_ORIGIN=*
```

Reiniciar backend.

### Paso 3: Configurar Unity para Móvil

En AuthManager script:
- Backend URL: `http://192.168.1.100:3000/api`
  (Cambiar IP por la tuya)

### Paso 4: Build y Test

1. Build APK
2. Instalar en Android
3. Asegurar que Android y PC estén en misma WiFi
4. Registrar cuenta desde móvil
5. Login desde móvil

---

## Seguridad para Producción

### Cuando lances el juego:

1. **Hosting del Backend**:
   - Heroku (gratis): https://heroku.com
   - Railway: https://railway.app
   - DigitalOcean: $5/mes
   - AWS/Google Cloud

2. **HTTPS Obligatorio**:
   - Usar certificado SSL
   - Backend URL: `https://tu-dominio.com/api`

3. **PostgreSQL en la Nube**:
   - Heroku Postgres (gratis hasta 10K rows)
   - Railway (incluido)
   - Supabase (gratis con buen límite)

4. **Variables de Entorno Seguras**:
   - JWT_SECRET: Generar string aleatorio de 64+ caracteres
   - DB_PASSWORD: Password seguro

5. **Rate Limiting**:
   - Agregar al backend para prevenir spam

---

## Troubleshooting

### Error: "Cannot connect to backend"

**En Editor:**
- Verificar que backend esté corriendo
- URL debe ser: `http://localhost:3000/api`

**En Android:**
- Verificar IP de tu PC
- Verificar firewall de Windows:
```powershell
New-NetFirewallRule -DisplayName "Node Backend" -Direction Inbound -Protocol TCP -LocalPort 3000 -Action Allow
```
- Android y PC en misma WiFi

### Error: "Database connection failed"

- Verificar PostgreSQL está corriendo
- Verificar password en .env
- Verificar database existe: `psql -U postgres -l`

### Error: "Username already exists"

- Normal, usuario ya está registrado
- Usar otro username o login con existente

### Passwords no coinciden

- Cliente valida antes de enviar
- Verificar RepeatPasswordField conectado

---

## Próximos Pasos

Una vez que el auth funcione:

1. ✅ Sistema de auth completamente funcional
2. ⏭️ Lobby scene con datos de usuario
3. ⏭️ Matchmaking con FishNet
4. ⏭️ Gameplay networked

**El auth está resuelto de forma profesional y escalable.** 🎯
