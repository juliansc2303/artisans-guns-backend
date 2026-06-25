# 🎮 LOADOUT API - DOCUMENTACIÓN

## 📋 RESUMEN
Sistema completo de loadout/configuración del jugador. Maneja selección de personaje, armas y skins desbloqueados.

---

## 🔐 AUTENTICACIÓN
Todos los endpoints requieren token JWT en el header:
```
Authorization: Bearer <token>
```

---

## 📡 ENDPOINTS

### 1. GET /api/loadout
Obtiene la configuración completa del usuario actual.

**Headers:**
```
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "success": true,
  "loadout": {
    "selectedCharacter": "CRIMSON",
    "level": 1,
    "primaryWeapon": {
      "weaponId": "rifle_phantom",
      "skinId": "default"
    },
    "secondaryWeapon": {
      "weaponId": "pistol_ghost",
      "skinId": "default"
    },
    "unlockedCharacters": ["CRIMSON"],
    "unlockedWeaponSkins": {
      "rifle_phantom": ["default"],
      "rifle_vandal": ["default"],
      "smg_stinger": ["default"],
      "pistol_ghost": ["default"],
      "pistol_sheriff": ["default"]
    }
  }
}
```

---

### 2. PUT /api/loadout
Actualiza la configuración del usuario (personaje y/o armas).

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Body (todos los campos opcionales):**
```json
{
  "selectedCharacter": "VIBE",
  "primaryWeapon": {
    "weaponId": "rifle_vandal",
    "skinId": "prime"
  },
  "secondaryWeapon": {
    "weaponId": "pistol_sheriff",
    "skinId": "default"
  }
}
```

**Response 200:**
```json
{
  "success": true,
  "loadout": {
    "selectedCharacter": "VIBE",
    "primaryWeapon": {
      "weaponId": "rifle_vandal",
      "skinId": "prime"
    },
    "secondaryWeapon": {
      "weaponId": "pistol_sheriff",
      "skinId": "default"
    },
    "level": 1
  }
}
```

**Response 400 (error):**
```json
{
  "success": false,
  "error": "Character not unlocked"
}
```

---

### 3. GET /api/loadout/inventory
Obtiene solo el contenido desbloqueado del usuario.

**Headers:**
```
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "success": true,
  "inventory": {
    "unlockedCharacters": ["CRIMSON", "VIBE"],
    "unlockedWeaponSkins": {
      "rifle_phantom": ["default", "prime", "reaver"],
      "rifle_vandal": ["default"],
      "pistol_ghost": ["default", "sovereign"]
    }
  }
}
```

---

### 4. POST /api/loadout/unlock-character
Desbloquea un personaje para el usuario.

**Uso:** Sistema de shop, recompensas, progression (futuro).  
**Actual:** Testing/Admin.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Body:**
```json
{
  "characterId": "VIBE"
}
```

**Response 200:**
```json
{
  "success": true,
  "unlockedCharacters": ["CRIMSON", "VIBE"]
}
```

---

### 5. POST /api/loadout/unlock-skin
Desbloquea un skin de arma para el usuario.

**Uso:** Sistema de shop, battle pass, drops (futuro).  
**Actual:** Testing/Admin.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Body:**
```json
{
  "weaponId": "rifle_phantom",
  "skinId": "prime"
}
```

**Response 200:**
```json
{
  "success": true,
  "unlockedWeaponSkins": {
    "rifle_phantom": ["default", "prime"],
    "pistol_ghost": ["default"]
  }
}
```

---

## 🎯 FLUJO DE USO EN UNITY

### Al iniciar sesión (LoginScene):
```csharp
// Login devuelve loadout completo
var response = await Login(username, password);

// Guardar en AuthManager
AuthManager.Instance.SetCurrentUser(response.user);

// user contiene:
// - selectedCharacter
// - level
// - primaryWeapon
// - secondaryWeapon
// - unlockedCharacters
// - unlockedWeaponSkins
```

### Al cargar LobbyScene:
```csharp
// Obtener datos del usuario
var user = AuthManager.Instance.GetCurrentUser();

// Configurar CharCard
CharCardController.Setup(
    user.selectedCharacter,
    user.primaryWeapon,
    user.secondaryWeapon,
    user.level
);
```

### Al cambiar personaje:
```csharp
// Usuario selecciona nuevo personaje
string newCharacter = "VIBE";

// Actualizar en backend
var response = await UpdateLoadout(new {
    selectedCharacter = newCharacter
});

// Actualizar localmente
user.selectedCharacter = newCharacter;
AuthManager.Instance.UpdateUser(user);

// Refrescar CharCard
CharCardController.Refresh();
```

### Al cambiar arma:
```csharp
// Usuario selecciona nueva arma
var newPrimary = new {
    weaponId = "rifle_vandal",
    skinId = "prime"
};

// Actualizar en backend
var response = await UpdateLoadout(new {
    primaryWeapon = newPrimary
});

// Actualizar localmente
user.primaryWeapon = newPrimary;
AuthManager.Instance.UpdateUser(user);

// Refrescar CharCard
CharCardController.Refresh();
```

### Al entrar a Room:
```csharp
// PlayerPrefab se configura con loadout del usuario
NetworkManager.SpawnPlayer(user.selectedCharacter, user.primaryWeapon, user.secondaryWeapon);
```

---

## 🗄️ ESTRUCTURA BASE DE DATOS

### Tabla: users
```sql
selected_character VARCHAR(50) DEFAULT 'CRIMSON'
level INTEGER DEFAULT 1
primary_weapon JSONB DEFAULT '{"weaponId": "rifle_phantom", "skinId": "default"}'
secondary_weapon JSONB DEFAULT '{"weaponId": "pistol_ghost", "skinId": "default"}'
unlocked_characters JSONB DEFAULT '["CRIMSON"]'
unlocked_weapon_skins JSONB DEFAULT '{
  "rifle_phantom": ["default"],
  "rifle_vandal": ["default"],
  "smg_stinger": ["default"],
  "pistol_ghost": ["default"],
  "pistol_sheriff": ["default"]
}'
```

---

## 🛡️ VALIDACIONES

### Cambiar personaje:
✅ Verifica que el personaje esté en `unlocked_characters`  
❌ Error si no está desbloqueado

### Cambiar skin de arma:
✅ Verifica que el weaponId exista en `unlocked_weapon_skins`  
✅ Verifica que el skinId esté en el array del weaponId  
❌ Error si no está desbloqueado

### Seguridad:
- Todas las validaciones se hacen en el backend
- No se puede "hackear" para obtener skins no desbloqueados
- Token JWT requerido para todas las operaciones

---

## 🚀 VALORES PREDETERMINADOS

Cuando un usuario se registra, recibe:
- **Personaje:** CRIMSON (desbloqueado)
- **Nivel:** 1
- **Arma primaria:** rifle_phantom (skin default)
- **Arma secundaria:** pistol_ghost (skin default)
- **Armas desbloqueadas:** 5 armas básicas con skin default cada una

---

## 📝 NOTAS IMPORTANTES

1. **Migración automática:** Si actualizas el backend, los usuarios existentes recibirán automáticamente los valores predeterminados.

2. **Login devuelve loadout:** No necesitas hacer GET /api/loadout después de login, ya viene en la respuesta.

3. **Actualización parcial:** Puedes actualizar solo el personaje, solo armas, o todo a la vez.

4. **Futuro:** Los endpoints de unlock están preparados para integrar con shop/progression system.

5. **ScriptableObjects:** El weaponId debe coincidir con el nombre del ScriptableObject en Unity.

---

## ✅ TESTING

### Postman Collection:
```
POST http://localhost:3000/api/auth/login
Body: { "username": "test", "password": "test123" }
→ Guarda el token

GET http://localhost:3000/api/loadout
Headers: Authorization: Bearer <token>
→ Ver loadout actual

PUT http://localhost:3000/api/loadout
Headers: Authorization: Bearer <token>
Body: { "selectedCharacter": "VIBE" }
→ Cambiar personaje
```
