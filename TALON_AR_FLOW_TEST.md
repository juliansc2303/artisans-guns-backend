# 🎯 TALON-AR COMPLETE FLOW TEST GUIDE

Guía para probar el flujo completo desde login hasta gameplay con **solo TalonAR**.

---

## 📋 SCOPE (Solo TalonAR)

### **Incluido en esta fase:**
- ✅ Login con usuario que tenga `talon_ar` como primaryWeapon
- ✅ LobbyScene mostrando TALON-AR en card
- ✅ GameScene spawn con TalonAR instanciada
- ✅ Animator controllers sincronizados (hands + weapon)
- ✅ IK grips funcionando
- ✅ Disparo básico con recoil
- ✅ Sistema de recarga (animaciones)

### **NO incluido (futuro):**
- ❌ Secondary weapon (Bolt pistol)
- ❌ Weapon switching
- ❌ Múltiples armas AR
- ❌ Sistema de munición/UI
- ❌ Jump/Shoot buttons

---

## 🔄 FLUJO COMPLETO

```
1. LoginScene
   └─ Usuario: "julian01" (o cualquiera con talon_ar)
   └─ Backend devuelve: primaryWeapon: { weaponId: "talon_ar", skinId: "default" }
   
2. LobbyScene
   ├─ LoadoutManager inicializa con data del backend
   ├─ UI muestra: "TALON-AR // 1"
   └─ PlayerCard display del character + weapon
   
3. Create Room → Enter Game
   ├─ NetworkManager spawns PlayerPrefab en GameScene
   └─ PlayerPrefab persiste (DontDestroyOnLoad)
   
4. GameScene
   ├─ PlayerSetup.Spawned() se ejecuta
   ├─ LoadWeaponsFromLoadout() lee "talon_ar"
   ├─ LoadWeaponConfigById() carga TalonAR.asset
   ├─ SpawnPrimaryWeapon() instancia TalonAR_Prefab
   ├─ ConfigureHandAnimations() aplica AR_Hands.controller
   ├─ UpdateIKTargets() posiciona grips
   └─ FireWeapon.Initialize() conecta FireButton
   
5. Gameplay
   ├─ Joystick: movimiento
   ├─ Touch right side: camera look
   ├─ FireButton: disparo
   └─ R key (temp): reload animation test
```

---

## ⚙️ SETUP REQUERIDO

### **1. Backend: Verificar Usuario**

Asegúrate que tu usuario de prueba tenga `talon_ar`:

```sql
-- Verificar en base de datos
SELECT username, primary_weapon_id, secondary_weapon_id 
FROM users 
WHERE username = 'julian01';

-- Resultado esperado:
-- username   | primary_weapon_id | secondary_weapon_id
-- julian01   | talon_ar          | bolt (o null por ahora)
```

---

### **2. Unity: TalonAR ScriptableObject**

`Assets/Resources/Weapons/TalonAR.asset`:

```
Weapon Id: talon_ar
Weapon Name: TALON-AR
Weapon Class: AssaultRifle

Weapon Prefab: TalonAR_Prefab (asignar tu prefab del arma)

Fire Rate: 600
Fire Sound: (asignar AudioClip)

Vertical Recoil: 1.2
Horizontal Recoil: 0.4
Recoil Recovery Speed: 3

Position Offset: (0.15, -0.1, 0.3)  // Ajustar según tu modelo
Rotation Offset: (0, 0, 0)
Scale Multiplier: (1, 1, 1)

Right Hand Grip Position: (0, 0, 0)       // Ajustar en testing
Left Hand Grip Position: (0, 0, -0.2)     // Ajustar en testing

Hands Animator Controller: AR_Hands.controller (crear en Assets/Animations/Controllers/)
Reload Hands Clip: (vacío por ahora)
Reload Time: 2.5
```

---

### **3. Unity: TalonAR_Prefab**

`Assets/Prefabs/Weapons/TalonAR_Prefab`:

```
TalonAR_Prefab (GameObject)
├─ TalonAR_Model (mesh del arma)
│  ├─ Body (mesh)
│  ├─ Magazine_Transform (empty, animado en reload)
│  └─ Bolt_Transform (empty, animado en reload)
├─ FirePoint (empty en punta del cañón)
├─ Animator (component)
│  └─ Controller: TalonAR_Weapon.controller
├─ FireWeapon (script)
│  └─ Fire Point: FirePoint (asignar)
├─ WeaponRecoil (script)
└─ AudioSource (component)
```

---

### **4. Unity: PlayerPrefab Setup**

`PlayerPrefab_TeamA` y `PlayerPrefab_TeamB`:

```
PlayerPrefab
├─ FPVCamera (main camera)
├─ CharacterController
├─ PlayerController (script)
├─ PlayerNetworkData (script)
├─ PlayerSetup (script) ← CONFIGURAR AQUÍ
│  ├─ Weapon Holder: WeaponHolder (transform)
│  ├─ Right Hand Grip: RightHandGrip (transform)
│  ├─ Left Hand Grip: LeftHandGrip (transform)
│  ├─ Rig Transform: mixamorig:Spine2 (transform)
│  └─ Hands Animator: mixamorig:Spine2 (animator component)
├─ WeaponHolder (empty transform)
│  └─ Position: Relativo a cámara (ej: 0.2, 0.5, 0.3)
├─ RightHandGrip (empty transform)
│  └─ Position: (0.15, 0.4, 0.25)
├─ LeftHandGrip (empty transform)
│  └─ Position: (0.05, 0.35, 0.4)
└─ mixamorig:Spine2 (contiene brazos)
   ├─ Rig Builder (component) ← Animation Rigging
   │  └─ TwoBoneIK constraints configurados
   └─ Animator (component) ← DEBE TENER
      └─ Controller: AR_Hands.controller (crear)
```

---

### **5. Unity: Animator Controllers**

#### **AR_Hands.controller** (Player)
`Assets/Animations/Controllers/AR_Hands.controller`:

```
Parameters:
└─ Reload (Trigger)

States:
├─ Idle_AR_Hands (Loop: Yes)
│  └─ AnimationClip: Idle_AR_Hands.anim (crear vacío temporalmente)
└─ Reload_AR_Hands (Loop: No, 2.5s)
   └─ AnimationClip: Reload_AR_Hands.anim (crear vacío temporalmente)

Transitions:
├─ Any State → Reload_AR_Hands (Condition: Reload, Exit Time: No)
└─ Reload_AR_Hands → Idle_AR_Hands (Exit Time: Yes, 1.0)
```

#### **TalonAR_Weapon.controller** (Weapon)
`Assets/Animations/Controllers/TalonAR_Weapon.controller`:

```
Parameters:
└─ WeaponReload (Trigger)

States:
├─ IdleTalon_AR (Loop: Yes)
│  └─ AnimationClip: IdleTalon_AR.anim (vacío)
└─ ReloadTalon_AR (Loop: No, 2.5s)
   └─ AnimationClip: ReloadTalon_AR_Magazine.anim (vacío)

Transitions:
├─ Any State → ReloadTalon_AR (Condition: WeaponReload, Exit Time: No)
└─ ReloadTalon_AR → IdleTalon_AR (Exit Time: Yes, 1.0)
```

---

### **6. GameScene: Canvas Setup**

`GameScene → Canvas`:

```
Canvas
├─ FloatingJoystick (ya existe)
├─ SettingsButton (ya existe)
└─ FireButton (CREAR)
   ├─ Nombre: "FireButton" (exacto)
   ├─ Posición: Bottom-right (ej: X=1100, Y=150)
   ├─ Tamaño: 100x100 px
   └─ NO asignar onClick (FireWeapon lo hace automático)
```

---

## 🧪 TESTING CHECKLIST

### **Phase 1: Login → Lobby** ✅

1. **Start Game** en Unity Editor
2. **LoginScene**: Ingresar credenciales
   - Username: `julian01` (o tu usuario de test)
   - Password: tu contraseña
3. **Click "LOGIN"**

**Expected Console Output**:
```
🔐 [AuthManager] Logging in user: julian01
✅ [AuthManager] Login successful: julian01
✅ [LoadoutManager] Loadout initialized for julian01
   Character: CRIMSON (Level 1)
   Primary: talon_ar - default
   Secondary: null
   💰 Currency: Blue Points=0, Rival Coins=0
```

4. **LobbyScene loads**
5. **Check UI**:
   - Player Card muestra: "TALON-AR // 1"
   - Character es visible (CRIMSON)

**✅ Pass**: Si ves "TALON-AR // 1" en UI → Backend connection OK

---

### **Phase 2: Lobby → GameScene** ✅

1. **En LobbyScene**, click "CREATE ROOM"
2. **Room created**, click "START GAME"
3. **GameScene carga**

**Expected Console Output**:
```
🔫 [PlayerSetup] Starting weapon setup for local player
✅ [PlayerSetup] Loaded primary weapon: talon_ar -> TALON-AR
🔫 [PlayerSetup] Weapon spawned and initialized: TALON-AR
🎬 [PlayerSetup] Applied hands animator: AR_Hands
🖐️ [PlayerSetup] Right hand grip: (0, 0, 0)
🖐️ [PlayerSetup] Left hand grip: (0, 0, -0.2)
🔫 [FireWeapon] Initialized: TALON-AR (Fire Rate: 600 RPM, Interval: 0.100s)
🎬 [FireWeapon] Hands animator: Connected
🎬 [FireWeapon] Weapon animator: Connected
✅ [FireWeapon] FireButton found and connected
```

4. **Check Scene**:
   - Player spawned en spawn point correcto (Team A o B)
   - TalonAR_Prefab visible como child de WeaponHolder
   - Brazos visibles (pueden verse deformados en scene view = normal)

**✅ Pass**: Si ves arma en WeaponHolder → Spawn OK

---

### **Phase 3: Gameplay - Movement** ✅

1. **En GameScene (mobile preview o builds)**:
   - Touch joystick: player se mueve
   - Touch right side: cámara rota
   - Movimiento es linear (digital)

**Expected Console Output** (cuando tocas):
```
✅ Joystick encontrado (intento 1): FloatingJoystick
🎯 Camera look iniciado en posición: ...
🎯 Camera look terminado
```

**✅ Pass**: Si player se mueve y cámara rota → Controls OK

---

### **Phase 4: Gameplay - Shooting** ✅

1. **Click FireButton** (UI bottom-right)
2. **Weapon fires**

**Expected Console Output**:
```
🔥 [FireWeapon] Fired TALON-AR
💥 [WeaponRecoil] Applied recoil: H=0.23, V=1.20
```

3. **Check Visuals**:
   - Camera recoil visible (sube ligeramente)
   - Audio de disparo (si asignaste AudioClip)
   - Raycast debug line (rojo si hit, amarillo si miss)

**✅ Pass**: Si suena y camera move → Fire OK

---

### **Phase 5: Gameplay - Reload (Temp)** 🔄

Por ahora, test manual con tecla:

1. **Agrega código temporal en FireWeapon.cs**:
```csharp
private void Update()
{
    // Test reload animation
    if (Input.GetKeyDown(KeyCode.R))
    {
        StartReload();
    }
    
    // Handle continuous fire if button is held
    if (isFiring)
    {
        Fire();
    }
}
```

2. **Presiona R** en Play Mode

**Expected Console Output**:
```
🔄 [FireWeapon] Player hands reload triggered
🔄 [FireWeapon] Weapon reload triggered
⏱️ [FireWeapon] Reload started, duration: 2.5s
(después de 2.5s)
✅ [FireWeapon] Reload complete
```

3. **Check Animator**:
   - Inspector → PlayerPrefab → mixamorig:Spine2 → Animator
   - Parámetro "Reload" se activa
   - Inspector → TalonAR_Prefab → Animator
   - Parámetro "WeaponReload" se activa

**✅ Pass**: Si ambos triggers activan → Sync OK

---

## ⚠️ COMMON ISSUES

### **Issue 1: "WeaponConfig not found for ID: talon_ar"**

**Causa**: `TalonAR.asset` no existe en `Assets/Resources/Weapons/`

**Fix**:
1. Verifica ruta: `Assets/Resources/Weapons/TalonAR.asset`
2. Verifica weaponId = `"talon_ar"` (exacto, case-sensitive)
3. Reimportar asset si es necesario

---

### **Issue 2: "Weapon spawned but not visible"**

**Causa**: Posición/escala incorrecta o modelo no asignado

**Fix**:
1. Inspector → WeaponHolder en Scene
2. Busca child `TalonAR_Prefab(Clone)`
3. Check `localPosition` y `localScale`
4. Ajusta `positionOffset` en `TalonAR.asset`

---

### **Issue 3: "Hands animator not connected"**

**Causa**: `handsAnimator` no asignado en PlayerSetup

**Fix**:
1. PlayerPrefab → PlayerSetup component
2. Hands Animator: Arrastra `mixamorig:Spine2` (el que tiene Animator component)
3. Verifica que Spine2 tenga Animator component con controller asignado

---

### **Issue 4: "FireButton not found"**

**Causa**: Button no existe o nombre incorrecto

**Fix**:
1. GameScene → Canvas → Crea Button
2. Nombre EXACTO: **"FireButton"** (case-sensitive)
3. No cambies nombre después

---

### **Issue 5: "Reload trigger does nothing"**

**Causa**: Animator controller no configurado correctamente

**Fix**:
1. Verifica Parameter "Reload" existe en AR_Hands.controller
2. Verifica Parameter "WeaponReload" existe en TalonAR_Weapon.controller
3. Verifica Transitions de Any State existen
4. Verifica AnimationClips asignados (pueden ser vacíos pero deben existir)

---

### **Issue 6: "Primary weapon is null despite backend data"**

**Causa**: LoadoutManager no inicializado o backend devuelve null

**Fix**:
1. Console: Busca "LoadoutManager initialized for [username]"
2. Verifica backend response tiene primaryWeapon.weaponId = "talon_ar"
3. Fallback a default debería funcionar si backend falla

---

## 📊 SUCCESS CRITERIA

Tu setup de TalonAR está completo cuando:

- ✅ Login successful, LoadoutManager muestra "talon_ar"
- ✅ LobbyScene UI muestra "TALON-AR // 1"
- ✅ GameScene spawns TalonAR_Prefab como child de WeaponHolder
- ✅ Console logs: "Weapon spawned and initialized: TALON-AR"
- ✅ Console logs: "Hands animator: Connected"
- ✅ Console logs: "Weapon animator: Connected"
- ✅ Console logs: "FireButton found and connected"
- ✅ Joystick movement funciona
- ✅ Camera look funciona
- ✅ FireButton dispara (recoil + audio)
- ✅ Presionar R activa ambos reload triggers
- ✅ Console muestra ambos "reload triggered" simultáneos

---

## 🚀 NEXT STEPS (Después de TalonAR 100%)

Una vez que todo lo anterior funcione perfectamente:

1. **Animar TalonAR weapon parts**:
   - Magazine sale/entra
   - Bolt cycles
   - Timing preciso

2. **Animar hands siguiendo weapon**:
   - IK targets interceptan magazine
   - Smooth transitions
   - Test en Game View (no Scene View)

3. **Agregar ReloadButton UI**:
   - Bottom-right, cerca de FireButton
   - Conectar con FireWeapon.StartReload()

4. **Polish shooting**:
   - Muzzle flash VFX
   - Bullet tracers
   - Hit markers
   - Sound effects

5. **Implementar Bolt (secondary weapon)**:
   - Crear Bolt.asset
   - Crear Pistol_Hands.controller
   - Weapon switching Q key
   - Test same flow

---

## 📝 NOTES

- **Focus**: Una arma perfecta > dos armas mediocres
- **Testing**: Probar cada phase antes de continuar
- **Debug**: Console logs son tus amigos
- **Iteration**: Ajustar position offsets iterativamente
- **Game View**: Siempre validar en Game View (FOV correcto)

---

✅ **Fase 1 completada** cuando puedas hacer el flujo completo sin errores
🎯 **Objetivo**: Login → Lobby → GameScene → Shoot → Reload (todo con TalonAR)
