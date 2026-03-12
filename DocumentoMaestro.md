# DOCUMENTO MAESTRO — Proyecto "Ryvalen" (Artisans Guns Dos)

> **Propósito:** Este documento es un prompt de contexto completo para cualquier sesión de IA futura. Contiene TODA la memoria acumulada sobre la arquitectura, decisiones técnicas, bugs resueltos, código creado, y estado actual del proyecto. Pásalo íntegro al inicio de una nueva conversación.

---

## 1. IDENTIDAD DEL PROYECTO

- **Nombre comercial:** Ryvalen
- **Nombre interno Unity:** Artisans Guns Dos
- **Género:** FPS táctico competitivo tipo Valorant, orientado a **móvil (Android)**
- **Engine:** Unity 6000.3.6f1
- **Networking:** Photon Fusion v4.1.8.16 — **Shared Mode** (client-authoritative, cada cliente controla su propio player)
- **Multi-editor testing:** ParrelSync (clones usan prefijo `"clone_"` en PlayerPrefs)
- **Backend:** Node.js + Express + PostgreSQL, desplegado en **Render.com**
- **Auth:** Guest auto → Google Sign-In (SDK 1.0.4) + Firebase Auth SDK 13.7.0
- **Workspace:** `c:\Users\julia\Artisans Guns Dos\`

---

## 2. ARQUITECTURA GENERAL

### 2.1 Escenas Unity
| Escena | Propósito |
|---|---|
| **LoginScene** | Autenticación (guest auto-backed por DB, o login/register) |
| **LobbyScene** | Sala de espera, selección de agente/armas, tienda, matchmaking |
| **GameScene** | Partida FPS activa, spawns, combate, ceremonia de conteo |

### 2.2 Stack tecnológico
```
┌─────────────────────────────────────────────┐
│  Unity Client (C#)                          │
│  ├─ Photon Fusion (Shared Mode, RPCs)       │
│  ├─ UI Toolkit (UXML + USS) — ALL UI        │
│  │   (HUD, Lobby, Joystick, Fire, Abilities) │
│  ├─ Google Sign-In SDK 1.0.4 (Android/iOS)  │
│  ├─ Firebase Auth SDK 13.7.0                │
│  └─ CharacterController — movimiento FPV    │
├─────────────────────────────────────────────┤
│  Backend (Node.js)                          │
│  ├─ Express + Helmet + CORS                 │
│  ├─ JWT auth (guest auto + Google + full)   │
│  ├─ PostgreSQL (Render.com hosted)          │
│  └─ REST API: /api/auth/* + /api/loadout/*  │
└─────────────────────────────────────────────┘
```

### 2.3 URLs del Backend
- **Editor (localhost):** `http://localhost:3000/api`
- **Build (producción):** `https://artisans-guns-api.onrender.com/api`
- Determinado por `#if UNITY_EDITOR` en AuthManager.cs, LoadoutManager.cs, y ShopTabController.cs

---

## 3. ESTRUCTURA DE CARPETAS (Scripts C#)

```
Assets/Scripts/
├─ Abilities/
│   ├─ AbilityConfig.cs              — Base ScriptableObject (abilityName, icon, cooldownSeconds)
│   ├─ AbilitySystem.cs              — NetworkBehaviour: orquesta abilities por character
│   ├─ SmokeGrenadeAbilityConfig.cs  — Config Crimson Ability 1
│   ├─ VisionPulseAbilityConfig.cs   — Config Crimson Ability 2
│   ├─ TsunamiWaveAbilityConfig.cs   — Config Pato Ability 1
│   ├─ WaterSuperJumpAbilityConfig.cs — Config Pato Ability 2
│   ├─ TsunamiWave.cs                — MonoBehaviour en prefab de la ola
│   ├─ CrimsonSmoke.cs               — Nube de humo (con ActiveSmoke estático)
│   ├─ GrenadeProjectile.cs          — Proyectil de granada física
│   └─ SmokeGrenadeAbility.cs        — FPV grenade equip/throw logic
├─ Auth/
│   ├─ AuthManager.cs                — Singleton, guest/login/register/Google, JWT, ParrelSync keys
│   └─ GoogleAuthService.cs          — Singleton, Google Sign-In SDK wrapper (Android/iOS + Editor mock)
├─ Characters/
│   ├─ CharacterConfig.cs            — SO: characterId, meshes, materials, abilities, deathVFX
│   └─ CharacterSetupHandler.cs      — NetworkBehaviour: aplica meshes + inicia AbilitySystem
├─ Data/
│   ├─ AgentDefinition.cs            — Registro estático de agentes (crimson, pato)
│   ├─ WeaponDefinition.cs           — Registro estático de armas (talon_ar, bolt)
│   ├─ WeaponConfig.cs               — SO Data (NO es el de Weapons/)
│   ├─ KnifeSkinDefinition.cs        — Skins de cuchillo
│   └─ ShopItemDefinition.cs         — Registro estático de items de tienda (skins comprables)
├─ Game/
│   ├─ PlayerController.cs           — CharacterController FPV, joystick, camera, jump, crouch
│   ├─ PlayerSetup.cs                — NetworkBehaviour: spawn weapons, IK, TPV, mesh refs
│   ├─ PlayerHealth.cs               — HP, damage RPC, death, respawn, immunity, PredictedHP
│   ├─ PlayerTPVController.cs        — Third person view: spine rotation, animations
│   ├─ PlayerTPVLocomotion.cs        — TPV locomotion animations + sounds
│   ├─ GameManager.cs                — Game loop management
│   ├─ GameUIManager.cs              — In-game UI management
│   ├─ MapSpawnManager.cs            — Spawn points por equipo
│   ├─ InputManager.cs               — Input system
│   ├─ TeamLayerAssigner.cs          — Asigna layers Enemy/Teammate
│   └─ TPVSoundRelay.cs              — Relay de sonidos TPV
├─ Networking/
│   ├─ NetworkManager.cs             — Singleton Fusion runner, room management, scene loading
│   ├─ PlayerNetworkData.cs          — Networked properties (Username, Team, Kills, Deaths, etc.)
│   └─ GameStateManager.cs           — Networked countdown, game state, RPC_ResetAllPlayers
├─ Weapons/
│   ├─ WeaponConfig.cs               — SO: weaponId, damage, fireRate, recoilPattern, prefabs, sounds
│   ├─ FireWeapon.cs                 — Raycast shooting, ammo, reload, muzzle flash, bullet trails
│   ├─ WeaponRecoil.cs               — Pattern-based recoil engine + counter-steer
│   ├─ RecoilPattern.cs              — SO: patternPoints[], counter-steer settings
│   ├─ WeaponSway.cs                 — Visual weapon sway + impulses
│   └─ BulletTrail.cs                — Trail visual effect
├─ UI/
│   ├─ MobileControlsController.cs   — Singleton, static events (OnFireDown, OnAbility1, OnAbility2, etc.)
│   ├─ AgentsTabController.cs        — Agent selection grid + Lock In button
│   ├─ ShopTabController.cs          — Shop tab: skin cards, buy button con currency icon, purchase API
│   ├─ WeaponsTabController.cs       — Weapon selection + skin picker
│   ├─ LobbyUIController.cs          — Lobby screen (Google promo, rooms overlay, overlays DOM)
│   ├─ GameplayHUDController.cs      — In-game HUD (HP, ammo, kill feed)
│   ├─ AuthUIController.cs           — Login/register UI
│   ├─ SettingsUIController.cs       — Settings panel (sensitivity, logout + OnLogoutPerformed event)
│   ├─ CrosshairManager.cs           — Crosshair rendering
│   ├─ KillFeedManager.cs            — Kill notifications
│   ├─ PersistentUIManager.cs        — UI that persists across scenes
│   ├─ AspectRatioManager.cs         — 16:9 aspect ratio enforcement
│   ├─ GameSceneAspectRatio.cs       — Game scene specific aspect
│   ├─ SceneAspectRatioSetup.cs      — Setup helper
│   ├─ CanvasAutoScaler.cs           — Canvas scaling
│   └─ GameSceneCanvasInitializer.cs — Canvas init for game scene
├─ Managers/
│   ├─ LoadoutManager.cs             — Loadout CRUD (character, weapons, skins, sensitivity)
│   ├─ SettingsManager.cs            — Settings management
│   └─ SoundManager.cs               — Audio management
├─ Loading/
│   └─ PreWarmManager.cs             — VFX pre-warming
└─ Debug/
    └─ ... (debug utilities)
```

---

## 4. BACKEND (Node.js)

### 4.1 Estructura
```
Backend/src/
├─ server.js                — Express app, middleware, routes
├─ database/
│   ├─ db.js                — PostgreSQL Pool, initDatabase(), migrations
│   └─ ADD_KNIFE_SUPPORT.sql — Migration SQL
├─ middleware/
│   └─ authMiddleware.js    — JWT verification middleware
├─ routes/
│   ├─ authRoutes.js        — POST /register, /login, /guest, /verify, /upgrade, /google-link, /google-login
│   └─ loadoutRoutes.js     — GET/PUT /loadout, GET /loadout/inventory, POST /loadout/purchase-skin
└─ services/
    ├─ authService.js       — register, login, guest, verify, upgrade, googleLink, googleLogin, verifyGoogleIdToken
    └─ loadoutService.js    — getLoadout, updateLoadout, getInventory, purchaseSkin
```

### 4.2 Esquema de DB (tabla `users`)
```sql
id SERIAL PRIMARY KEY,
username VARCHAR(50) UNIQUE NOT NULL,
password_hash VARCHAR(255) NOT NULL,
character_name VARCHAR(50) NOT NULL,
selected_character VARCHAR(50) DEFAULT 'CRIMSON',
level INTEGER DEFAULT 1,
primary_weapon JSONB DEFAULT '{"weaponId":"talon_ar","skinId":"default"}',
secondary_weapon JSONB DEFAULT '{"weaponId":"bolt","skinId":"default"}',
knife_skin JSONB,
unlocked_characters JSONB DEFAULT '["CRIMSON"]',
unlocked_weapon_skins JSONB DEFAULT '{...}',
blue_points INTEGER DEFAULT 1000,        -- ← Rival Essence (was 0, now 1000 para nuevos usuarios)
rival_coins INTEGER DEFAULT 0,            -- ← Rival Points
google_id VARCHAR(128) UNIQUE,            -- ← Google account link (NULL si no vinculado)
sensitivity FLOAT DEFAULT 6.0,
is_guest BOOLEAN DEFAULT FALSE,
guest_uuid VARCHAR(64) UNIQUE,
created_at TIMESTAMP,
last_login TIMESTAMP,
is_active BOOLEAN DEFAULT TRUE
```

### 4.3 Defaults para registro nuevo (authService.js)
```js
selected_character: 'crimson',
unlocked_characters: ['crimson', 'pato'],   // Ambos desbloqueados al inicio
primary_weapon: { weaponId: 'talon_ar', skinId: 'default' },
secondary_weapon: { weaponId: 'bolt', skinId: 'default' }
```

### 4.4 Defaults para guest (authService.js)
```js
selected_character: 'crimson',
unlocked_characters: ['crimson', 'vibe', 'sight', 'pato'],
// Guests get all characters to try
```

### 4.5 Validación del loadout update (loadoutService.js)
- Verifica `selectedCharacter` esté en `unlocked_characters` (**case-insensitive**: `.some(c => c.toLowerCase() === lowerSelected)`)
- Verifica skins de armas estén desbloqueadas (default siempre válida)
- Dynamic query builder para UPDATE parcial

### 4.6 Google Auth endpoints (authRoutes.js + authService.js)

**POST `/auth/google-link`** (Requires Bearer token)
- Body: `{ googleIdToken, characterName }`
- Verifica el token de Google → extrae `googleId`
- Vincula la cuenta guest actual a Google (`UPDATE users SET google_id, character_name, is_guest=false`)
- Retorna nuevo JWT + UserData (ahora es cuenta registrada)
- `verifyGoogleIdToken()`: DEV MODE acepta tokens `EDITOR_TEST_*`, PRODUCCIÓN usa endpoint `https://oauth2.googleapis.com/tokeninfo`

**POST `/auth/google-login`** (Público, sin token)
- Body: `{ googleIdToken }`
- Verifica el token → busca user por `google_id`
- Si encuentra usuario → retorna JWT + UserData
- Si no encuentra → retorna error (usuario debe hacer link primero)

### 4.7 Purchase-skin endpoint (loadoutRoutes.js + loadoutService.js)

**POST `/loadout/purchase-skin`** (Requires Bearer token)
- Body: `{ weaponId, skinId, price, currencyType }` (currencyType = "blue_points" | "rival_coins")
- Validaciones: precio > 0, skin no ya comprada ("Skin already owned"), balance suficiente
- Operación atómica: deduce currency + añade skin a `unlocked_weapon_skins` del arma
- Retorna `{ success, newBalance, unlocked_weapon_skins }`

---

## 5. SISTEMA DE NETWORKING (Photon Fusion Shared Mode)

### 5.1 Conceptos clave
- **Shared Mode:** Cada cliente tiene InputAuthority + StateAuthority sobre su propio player
- **RPCs:** `[Rpc(RpcSources.InputAuthority, RpcTargets.All)]` para acciones que todos deben ver
- **[Networked] properties:** Se sincronizan automáticamente (HP, IsDead, Team, Kills, Deaths, etc.)
- **No hay server autoritativo** — cada cliente controla su player

### 5.2 PlayerNetworkData.cs — Props networked
```csharp
[Networked] Username, CharacterName, SelectedAgent, CharacterType
[Networked] IsReady, InGame, Team (0=A, 1=B), JoinOrder, TeamAssigned
[Networked] Level, PrimaryWeapon, SecondaryWeapon, KnifeWeapon
[Networked] Kills, Deaths
```

- **PlayerCache:** `Dictionary<PlayerRef, PlayerDataSnapshot>` estático — cachea datos de todos los jugadores
- **CharacterType:** 0=CRIMSON, 1=VIBE, 2=SIGHT, 3=PATO

### 5.3 GameStateManager.cs — Estado de partida
```csharp
[Networked] CountdownValue  // -1=not started, 3-2-1=countdown
[Networked] CountdownStarted
[Networked] GameInProgress
```
- `StartCountdown()` — Solo host, sets countdown=3, broadcast RPC_ResetAllPlayers
- `RPC_ResetAllPlayers()` — Cada cliente resetea su propio player (HP, position, kills, deaths)

### 5.4 Patrón de RPC para abilities
Las abilities se spawnan con `Instantiate` (NO `Runner.Spawn`) dentro de RPCs:
```
InputAuthority client → RPC → ALL clients → cada uno hace Instantiate del prefab
```
- Misma posición y parámetros = mismo resultado visual
- Solo el InputAuthority ejecuta lógica de gameplay (rider tracking, detonation, etc.)
- Remote clients cargan prefabs desde `Resources.Load<CharacterConfig>(...)` como fallback

---

## 6. SISTEMA DE LAYERS Y COLISIONES

### 6.1 Layers activos
| Layer | Uso |
|---|---|
| **Default (0)** | Entorno, suelos, paredes |
| **Player** | El jugador LOCAL en cada cliente |
| **Enemy** | Jugadores del equipo contrario (remoto) |
| **Teammate** | Jugadores del mismo equipo (remoto) |
| **InmunePlayer** | Player durante inmunidad post-respawn |
| **Water** | Colliders de la TsunamiWave (ola de Pato) |
| **Layer 6** | Armas FPV (solo renderizadas por cámara FPV) |

### 6.2 Asignación de layers
- **PlayerHealth.cs:** `gameObject.layer = LayerMask.NameToLayer("Player")` para local
- **TeamLayerAssigner.cs:** Asigna Enemy o Teammate para remotos según Team
- **InmunePlayer:** Cambiado temporalmente durante los 3s de inmunidad post-respawn
- **Water:** Asignado en el prefab TsunamiVFX (hijos WaterCollider, WaterCollider(1), WaterCollider(2))

### 6.3 Hit layer mask (FireWeapon.cs)
```csharp
hitLayerMask = (1 << 0);           // Default (environment)
hitLayerMask |= (1 << enemyLayer); // Enemy players
hitLayerMask |= (1 << waterLayer); // Water (wave shield — blocks bullets)
```
- Las balas **SIEMPRE** van donde apunta la mira (no hay spread)
- El recoil mueve la CÁMARA, no la trayectoria de la bala

### 6.4 Collision Matrix para Water layer
- Water **SOLO** colisiona con Player layer en la physics matrix de Unity
- PERO `Physics.IgnoreCollision()` se llama entre TODOS los colliders de la ola y TODOS los CharacterControllers → ningún jugador choca físicamente con la ola
- Las balas (raycast) SÍ impactan la ola porque `Physics.IgnoreCollision` NO afecta raycasts

---

## 7. PERSONAJES (AGENTES)

### 7.1 Registro de agentes (AgentDefinition.cs)
```csharp
new Agent("crimson", "CRIMSON", AgentRole.Duelist, "Icons/CrimsonIcon", true),
new Agent("pato",    "PATO",    AgentRole.Duelist, "Icons/PatoIcon",    true)
```
- `agentId` siempre en **lowercase**
- `isDefault = true` → desbloqueado al inicio

### 7.2 CharacterConfig (ScriptableObject)
Archivo: `Resources/Characters/<characterId>.asset` (e.g., `crimson.asset`, `pato.asset`)
```
characterId: string
tpvMesh, tpvMaterials     — cuerpo visible para otros
armsMesh, armsMaterials   — brazos FPV visibles para el local
ability1: AbilityConfig   — Ability 1
ability2: AbilityConfig   — Ability 2
deathVFXPrefab, deathVFXDuration
```

### 7.3 CharacterSetupHandler.cs
- En `Spawned()` (local) y `Render()` (retry): carga CharacterConfig, aplica meshes, llama `AbilitySystem.Initialize(cfg)`
- Local: obtiene characterId de `LoadoutManager.Instance.GetLoadout().selectedCharacter`
- Remote: obtiene de `PlayerNetworkData.SelectedAgent`
- Intenta lowercase primero, luego capitalizado como fallback

---

## 8. SISTEMA DE ABILITIES

### 8.1 AbilitySystem.cs — Orquestador central
- **NetworkBehaviour** en el player prefab, junto a PlayerSetup
- Solo activo para `InputAuthority` (local player)
- Detecta el tipo de abilities en `Initialize()`:

```csharp
enum AbilitySet { None, Crimson, Pato }

// En Initialize():
smokeConfig    = config.ability1 as SmokeGrenadeAbilityConfig;
pulseConfig    = config.ability2 as VisionPulseAbilityConfig;
tsunamiConfig  = config.ability1 as TsunamiWaveAbilityConfig;
superJumpConfig = config.ability2 as WaterSuperJumpAbilityConfig;

if (tsunamiConfig != null && superJumpConfig != null) → Pato
else if (smokeConfig != null && pulseConfig != null) → Crimson
```

- `OnAbility1Pressed()` / `OnAbility2Pressed()` → switch por `_activeSet`
- Suscrito a `MobileControlsController.OnAbility1` / `OnAbility2` (static events)
- Iconos de abilities set en HUD via `MobileControlsController.Instance.SetAbility1Icon()`

### 8.2 Crimson — Smoke Grenade (Ability 1)
- **SmokeGrenadeAbilityConfig:** grenadeFPVPrefab, grenadesHandsAnimatorController, grenadeProjectilePrefab, throwSpeed, smokePrefab, smokeDuration, grenadePrefabTPV, postureAnimatorControllerTPV
- **Flow:** Press Ability1 → EquipAbilityItem (swap weapon for grenade FPV) → HijackFireButton → Player throws → RPC_SpawnProjectile → GrenadeProjectile lands → RPC_SpawnSmoke → CrimsonSmoke instantiated
- **CrimsonSmoke:** Has `ActiveSmoke` static ref

### 8.3 Crimson — Vision Pulse (Ability 2)
- **VisionPulseAbilityConfig:** specific pulse settings
- **Requires** active CrimsonSmoke in scene (`CrimsonSmoke.ActiveSmoke`)
- Triggers pulse effect through smoke cloud

### 8.4 Pato — Tsunami Wave (Ability 1)
- **TsunamiWaveAbilityConfig:** wavePrefab, waveSpeed=14, waveDuration=3, riseFromBelow=3, riseSpeed=12, riderHeightOffset=1.0, spawnSound
- **Requisito:** Must be grounded to cast
- **Spawn origin:** Raycast hacia abajo para encontrar la superficie real → `surfaceY + 0.1` (nunca clipea con el suelo)
- **Flow:**
  1. `ActivateTsunamiWave()` → raycast down, get surface Y + 0.1
  2. Get camera forward (projected to XZ plane)
  3. `RPC_SpawnTsunamiWave(spawnPos, direction)` → ALL clients
  4. Each client: `Instantiate(wavePrefab)`, configure `TsunamiWave` component, call `Launch(spawnPos)`
  5. Only InputAuthority gets `riderController` + `riderPlayerController` assigned → rides the wave
  6. Remote clients load prefab desde CharacterConfig como fallback

- **TsunamiWave.cs (MonoBehaviour en el prefab):**
  - `Launch(spawnOrigin)`: `_targetY = spawnOrigin.y`, starts below (`-riseFromBelow`), rises up
  - `Update()`: rise phase → forward movement → rider tracking
  - **Rider tracking:**
    - Moves CharacterController alongside wave via `cc.Move(platformDelta)`
    - Snaps rider Y to wave top + `riderHeightOffset`
    - Dismount detection: if rider walks off (XZ distance > 5 units)
  - **IgnoreAllPlayerCollisions():** `Physics.IgnoreCollision` entre TODOS los wave colliders y TODOS los CharacterControllers → nadie choca físicamente con la ola, pero raycasts (balas) SÍ impactan
  - `ActiveRiderWave` (static): referencia al wave activo del rider local, usado por Ability 2
  - Auto-destroy después de `waveDuration` seconds

### 8.5 Pato — Water Super Jump (Ability 2)
- **WaterSuperJumpAbilityConfig:** jumpForce=18, jumpSound
- **Requisito:** `TsunamiWave.ActiveRiderWave != null && .IsRiding`
- **Flow:**
  1. Check standing on wave
  2. `activeWave.DismountRider()` — detach from wave
  3. `PlayerController.SetVelocityY(jumpForce)` — launch upward
  4. Start cooldown

### 8.6 Cooldown system
- `StartCooldown(int slot, float seconds)` → coroutine with radial UI progress
- `CooldownUICoroutine`: updates `MobileControlsController` dial progress + color
- Colors: Green (ready) → Orange (cooldown)

---

## 9. SISTEMA DE MOVIMIENTO (PlayerController.cs)

### 9.1 Componentes clave
- `CharacterController characterController` (Unity built-in)
- `isGrounded` = `characterController.isGrounded` each tick
- `velocity` (Vector3) — only Y used for gravity/jump
- `gravity = -33f`

### 9.2 FixedUpdateNetwork flow
```
1. Guard: !HasInputAuthority, InputFrozen, cc disabled → skip
2. isGrounded = cc.isGrounded
3. Track highest Y while airborne (for land detection)
4. Detect landing (isGrounded && !_wasGrounded) → play land sound
5. Gravity: grounded && velocity.y < 0 → velocity.y = -2f, else += gravity * dt
6. Read MoveInput from MobileControlsController.Instance.MoveInput
7. Transform to world space, apply crouch penalty (45%)
8. cc.Move(move * effectiveSpeed * dt)
9. Jump: if jumpInput && isGrounded && !crouching → velocity.y = sqrt(jumpForce * -2 * gravity)
10. cc.Move(velocity * dt)
11. Apply camera rotation (pitch + recoil, yaw + recoil)
12. Sync NetworkPosition, NetworkRotation, NetworkPitch, NetworkAnimState
```

### 9.3 Propiedades públicas importantes
- `IsGrounded` — bool property
- `IsCrouching` — bool
- `DidJump` — true for one tick after jump
- `SetVelocityY(float)` — sets velocity.y directly (for abilities like super jump)
- `UpdateWeaponSpeedModifier(float)` — called by PlayerSetup on weapon equip
- `SetLookSensitivity(float)` — from settings
- `GetCameraPitch()` — for TPV spine rotation
- `GetCameraDelta()` — for weapon sway/recoil

### 9.4 Static flag
- `InputFrozen` — when true, all input is blocked (used during ceremony countdown)

---

## 10. SISTEMA DE ARMAS

### 10.1 WeaponConfig (ScriptableObject)
Cada arma tiene un WeaponConfig con:
- Identity: `weaponId`, `weaponName`, `isKnife`, `weaponClass`
- Fire: `fireRate`, `isAutomatic`, `maxAmmo`, `bulletRange`, `damage`, `headshotMultiplier`
- Recoil: `recoilPattern` (RecoilPattern SO), legacy fallback fields
- Visual: `weaponPrefab`, `muzzleFlashPrefab`, `tpvMuzzleFlashPrefab`, `impactEffectPrefab`
- Audio: `fireSound`, `fireSoundTPV`, `reloadSounds[]`, `emptyMagazineSound`, `impactSound`
- Movement: `speedMultiplier`
- Position: `positionOffset`, `rotationOffset`, `scaleMultiplier`
- TPV: `prefabWeaponTPV`, `handsAnimatorControllerTPV`
- Grip: Prefabs must have `RightHandGrip`/`LeftHandGrip` (FPV) and `RightGrip`/`LeftGrip` (TPV)
- Blood: `headBloodPrefab`, `bodyBloodPrefab`
- Trail: `bulletTrailMaterial`, `trailFlashDuration`, `trailShrinkSpeed`, `trailWidthFar`, `trailWidthNear`

### 10.2 Armas registradas (WeaponDefinition.cs)
```
Primary:  talon_ar  (TALON-AR) — Assault Rifle, default
Secondary: bolt     (BOLT) — Pistol, default
Knife:    (melee, always available)
```

### 10.3 FireWeapon.cs — Shooting
- Center-screen raycast from camera
- `hitLayerMask`: Default + Enemy + Water
- Damage via `RPC_TakeDamage` al PlayerHealth de la víctima
- Muzzle flash, bullet trail, impact VFX
- Ammo tracking, reload animation events

### 10.4 RecoilPattern.cs + WeaponRecoil.cs — Sistema de recoil
**RecoilPattern (ScriptableObject):**
- `patternPoints[]` — Vector2 array (X=horizontal, Y=vertical kick per shot)
- `loopPattern` — if true, loops; if false, last point repeats
- Counter-steer settings: `counterSteerMinMultiplier`, `wrongSteerAmplification`, `counterSteerDeadZone`
- Movement modifiers: `movingMultiplier`, `movingHorizontalRange`
- Context menus para generar patterns default

**WeaponRecoil.cs — Pattern engine:**
- `_patternIndex` advances one step per shot
- Reset after pause (no shots for > `_fireInterval * 3`)
- Counter-steer: compares camera drag direction vs next kick via dot product
  - Perfect counter → kick multiplied by `counterSteerMinMultiplier` (0.3 = 70% reduction)
  - Wrong steer → kick multiplied by `wrongSteerAmplification` (1.5 = 50% increase)
- Pending kicks (`_pendingPitchKick`, `_pendingYawKick`) drained smoothly over frames
- `ResetPattern()` — called on reload (from `FireWeapon.cs`)
- Legacy fallback: if no RecoilPattern assigned, uses old `recoilKickAmount` from WeaponConfig

---

## 11. SISTEMA DE SALUD Y COMBATE (PlayerHealth.cs)

### 11.1 Stats
- `MAX_HP = 150f`
- `RESPAWN_SECONDS = 3f`
- `IMMUNITY_SECONDS = 3f` (post-respawn)

### 11.2 Networked state
```csharp
[Networked] HP
[Networked] IsDead
```

### 11.3 PredictedHP system
- `PredictedHP` — local-only float, tracks damage before network sync
- `PredictedDead` — `PredictedHP <= 0`
- Prevents overkill (multiple blood VFX on already-dead player)

### 11.4 Security model (Shared Mode)
1. **Shooter** validates hit locally (raycast + layer mask + team check)
2. Sends `RPC_TakeDamage` to **victim's** NetworkObject
3. **Victim** applies damage to own `[Networked] HP`
4. On death: victim broadcasts `RPC_Die` → all clients play VFX / hide model
5. Kill/death counters on each player's own PlayerNetworkData

### 11.5 Inmunidad
- Post-respawn: 3 seconds con material especial (green outline)
- Layer cambiado a "InmunePlayer" temporalmente
- Restored to "Player" layer after immunity expires

### 11.6 Death overlay
- UIToolkit VisualElement (red tint + respawn text)
- `pickingMode = Ignore` — no absorbe touch events
- `FireWeapon.IsDead` guard blocks shooting

---

## 12. SISTEMA DE UI

### 12.1 UI System (100% UI Toolkit)
- **UI Toolkit (UXML + USS):** Todo el UI — Login, Lobby, GameplayHUD, Settings, controles in-game (Joystick, Fire, Abilities, Weapon switch)
- *Nota: se migró completamente de uGUI Canvas a UI Toolkit*

### 12.2 Archivos UI importantes
```
Assets/UI/
├─ Auth/LoginScreen.uxml + .uss
├─ Lobby/LobbyScreen.uxml + .uss
├─ Lobby/WeaponsTab.uxml + .uss
├─ Game/GameplayHUD.uxml + .uss
├─ Game/MobileControls.uxml
├─ Game/SettingsPanel.uxml + .uss
└─ PersistentUI.uxml + .uss
```

### 12.3 MobileControlsController.cs — Static events
```csharp
public static event Action OnFireDown, OnFireUp;
public static event Action OnReload;
public static event Action OnKnifeSelect, OnPrimarySelect, OnSecondarySelect;
public static event Action OnAbility1, OnAbility2;
public static event Action OnJump, OnCrouch, OnStand;
public Vector2 MoveInput { get; private set; }  // Joystick input
```
- `SetFireOverride(Action, Action)` — hijacks fire button (used for grenade throw)
- `ClearFireOverride()` — restores normal fire
- `SetAbility1Icon/2Icon`, `SetAbility1Progress/2Progress`, `SetAbility1Interactable/2Interactable`

### 12.4 AgentsTabController.cs — Selección de agentes
- Grid horizontal con cards (90×110px each)
- CSS: `.agent-card-selected` → green outline + icon scale 1.08
- CSS: `.agent-card-current` → different visual
- Lock In button → calls `LoadoutManager.Instance.UpdateCharacter(agentId)`
- Comprueba `IsAgentUnlocked()` (case-insensitive)

### 12.5 Aspect ratio
- 16:9 enforced via `AspectRatioManager.cs` + `GameSceneAspectRatio.cs`
- Black bars on non-16:9 screens

### 12.6 ShopTabController.cs — Tienda de skins
- `PopulateShop()`: Carga items estáticos de `ShopItemDefinition`, verifica ownership con `LoadoutManager.IsSkinUnlocked()`
- `CreateShopCard()`: Muestra nombre, icono, precio + icono de moneda (64×64px, a la DERECHA del precio)
  - Items comprados muestran "OWNED" en vez de precio
  - Click selecciona la card (no compra directo)
- `UpdateBuyButton()`: Reconstruye el botón con child elements: `Label("BUY — 1000")` + currency icon (64px, a la derecha)
  - Limpia todo con `buyButton.text = ""; buyButton.Clear()` y reconstruye con VisualElements internos
  - Botón grande: 360×64px
  - Sin selección: muestra "SELECT AN ITEM" (disabled)
  - Sin fondos: muestra "NOT ENOUGH — precio" (disabled)
- `PurchaseItem()`: POST a `/loadout/purchase-skin`, luego `RefreshLoadout()` → `PopulateShop()`
- Monedas: `bluePoints` = Rival Essence, `rivalCoins` = Rival Points

### 12.7 ShopItemDefinition.cs — Registro de items
```csharp
// CurrencyType: RivalEssence, RivalPoints
new ShopItem("shop_talon_skull", "talon_ar", "talon_skull", "TALON-SKULL", "Icons/TalonSkullIcon", 1000, CurrencyType.RivalEssence),
```
- Solo un item por ahora (Talon-Skull, 1000 Rival Essence)
- `GetAllItems()`, `GetItem(itemId)`

### 12.8 LobbyScreen.uxml — Estructura DOM del Lobby
**Jerarquía de overlays (TODOS al nivel de MainContainer, después de GooglePromoPanel):**
```
MainContainer/
  ├─ ... (header, tabs, content)
  ├─ GooglePromoPanel           — Bottom-left promo: "Sign up with Google & earn 1,000 [icon]" + "GET STARTED"
  ├─ RoomPanelOverlay           — Modal backdrop (full-screen dark) wrapping RoomPanel (780×530px)
  │   └─ RoomPanel              — "AVAILABLE ROOMS" + ScrollView + close button
  ├─ CreateRoomOverlay          — "CUSTOM GAME" (was "PRIVATE ROOM")
  │   ├─ Section "CREATE A ROOM" — gamemode selector + map selector + create button
  │   └─ Section "JOIN WITH CODE" — hint text + code TextField + join button
  ├─ SaveProgressOverlay        — "LINK YOUR ACCOUNT" + SignUpButton.png image (650px panel)
  ├─ CharacterNameOverlay       — Shown after Google Sign-In link: enter character name
  └─ LoginOverlay               — "WELCOME BACK" + SignInButton.png image (650px panel)
```
**IMPORTANTE:** El orden de overlays en el DOM importa para z-order. Todos van DESPUÉS de GooglePromoPanel.

**Botones secundarios del lobby:**
- "CREATE / JOIN" (antes "PRIVATE ROOM") — abre CreateRoomOverlay
- "ROOMS" — abre RoomPanelOverlay (lista de salas disponibles)

### 12.9 LobbyScreen.uss — Estilos clave actualizados
```css
/* Shop */
.shop-card-currency-icon { width: 64px; height: 64px; margin-left: 6px; }
.shop-buy-button { width: 360px; height: 64px; }
.shop-buy-currency-icon { width: 64px; height: 64px; margin-left: 8px; }
.shop-buy-label { color: white; font-size: 18px; letter-spacing: 3px; -unity-font-style: bold; }

/* Google Promo */
.google-promo-essence-icon { width: 64px; height: 64px; }

/* Room Panel Modal */
.room-popup-overlay { position: absolute; width: 100%; height: 100%; background-color: rgba(0,0,0,0.75); }
.room-popup-panel { width: 780px; height: 530px; max-width: 90%; background-color: rgba(12,12,18,0.97); border-color: purple; }
.room-popup-title { font-size: 16px; color: purple; letter-spacing: 3px; }
.room-item { border-color: purple; /* hover: purple background */ }

/* Google Button Images (SaveProgress + Login overlays) */
.google-signin-image { width: 179px; height: 40px; }
/* Assets: Resources/Buttons/SignUpButton.png, SignInButton.png */
```

### 12.10 LobbyUIController.cs — Controlador principal del Lobby
**Google Sign-In flow (Save Progress):**
1. Guest ve `GooglePromoPanel` (bottom-left) → click "GET STARTED"
2. Se abre `SaveProgressOverlay` con imagen SignUpButton.png
3. Click en imagen → `OnGoogleSignInForLink()` → `GoogleAuthService.Instance.SignIn()`
4. Google SDK retorna idToken → `AuthManager.GoogleLink(idToken, characterName)`
5. Backend vincula Google → `OnGoogleLinkSuccess` + `OnLoginSuccess` fired
6. Si necesita character name → se muestra `CharacterNameOverlay`

**Google Login flow (cuenta existente):**
1. Desde `LoginOverlay`, click en imagen SignInButton.png
2. `GoogleAuthService.Instance.SignIn()` → `AuthManager.GoogleLogin(idToken)`
3. Backend busca por google_id → `OnGoogleLoginSuccess` + `OnLoginSuccess` fired

**Rooms panel:**
- `ToggleRoomList()`: Toggle visibility de `RoomPanelOverlay` (no `RoomPanel`)
- `CloseRoomPanelButton`: Registrado en `RegisterEvents()` → añade "hidden" a `RoomPanelOverlay`

**Logout event chain:**
- `SettingsUIController.OnLogoutPerformed` → `LobbyUIController.OnLogoutPerformed()`
- `OnLogoutPerformed()`: Muestra GooglePromoPanel, oculta logout button, actualiza character display

---

## 13. SISTEMA DE AUTENTICACIÓN (AuthManager.cs)

### 13.1 Modos
```csharp
enum AuthMode { Guest, LoggedIn }
```

### 13.2 Flow Guest (.IO style)
1. Check for saved `auth_token` in PlayerPrefs
2. If exists → `VerifyToken()` → restore session
3. If not → Generate random `guest_uuid` (8 chars) → `POST /api/auth/guest`
4. Backend creates DB row con `is_guest=true` → returns JWT + UserData
5. Fallback: si backend unreachable → local-only guest

### 13.3 ParrelSync safety
- `_keyPrefix`: "" for main editor, "clone_" for ParrelSync clones
- All PlayerPrefs keys wrapped with `K(key)` → `_keyPrefix + key`
- Each clone gets unique guest identity

### 13.4 Upgrade flow
- Guest can upgrade to full account (`POST /api/auth/upgrade`)
- Keeps same DB row, adds username + password
- **O bien:** Google Sign-In link (`POST /api/auth/google-link`) — mismo concepto, sin username/password

### 13.5 Google Sign-In flow (GoogleAuthService.cs + AuthManager.cs)

**GoogleAuthService.cs** — Singleton, DontDestroyOnLoad
- `WEB_CLIENT_ID`: `329775748159-oj54pn4q1l2e13khrkfk76105117q18n.apps.googleusercontent.com`
- `SignIn()`: Lanza popup de Google OAuth
  - **EDITOR:** Mock con `EDITOR_TEST_` + `SystemInfo.deviceUniqueIdentifier` → OnGoogleSignInSuccess directamente
  - **Android/iOS:** Google SDK real → `HandleSignInResult()` vía `UnityMainThreadDispatcher.Enqueue()`
- Events: `OnGoogleSignInSuccess(idToken)`, `OnGoogleSignInFailed(error)`
- `SignOut()`: Cierra sesión de Google

**AuthManager.cs — Google Link (guest → registered):**
1. `GoogleLink(googleIdToken, characterName)` → `GoogleLinkCoroutine()`
2. POST `/auth/google-link` con Bearer token + body
3. Success → actualiza currentToken/currentUser, `AuthMode = LoggedIn`, `isGuestSession = false`
4. Fires: `OnGoogleLinkSuccess` → `OnLoginSuccess` (AMBOS se disparan)
5. Limpia `guest_id` de PlayerPrefs

**AuthManager.cs — Google Login (cuenta existente):**
1. `GoogleLogin(googleIdToken)` → `GoogleLoginCoroutine()`
2. POST `/auth/google-login` (SIN Bearer token — endpoint público)
3. Success → actualiza currentToken/currentUser, `AuthMode = LoggedIn`
4. Fires: `OnGoogleLoginSuccess` → `OnLoginSuccess` (AMBOS se disparan)
5. Limpia `guest_id` de PlayerPrefs

**⚠️ CRÍTICO:** `OnLoginSuccess` se dispara DESPUÉS de `OnGoogleLinkSuccess`/`OnGoogleLoginSuccess`. Esto es necesario porque `LoadoutManager` solo escucha `OnLoginSuccess` y `OnGuestReady`, NO los eventos de Google directamente.

### 13.6 Logout flow
- `SettingsUIController.OnLogoutClicked()` → `AuthManager.Instance.Logout()`
- `Logout()` → `ClearSavedAuth()` → elimina TODOS los PlayerPrefs keys (`auth_token`, `user_*`, `guest_id`) → `InitializeGuestFromBackend()`
- Después de Logout, `OnLogoutPerformed?.Invoke()` → `LobbyUIController` actualiza UI (muestra GooglePromoPanel, oculta logout, etc.)

### 13.7 Events completos
```csharp
// Auth core
OnLoginSuccess, OnLoginFailed
OnRegisterSuccess, OnRegisterFailed
OnTokenExpired
OnGuestReady
OnUpgradeSuccess, OnUpgradeFailed
OnConnectionFailed

// Google
OnGoogleLinkSuccess, OnGoogleLinkFailed
OnGoogleLoginSuccess, OnGoogleLoginFailed
```

### 13.8 DontDestroyOnLoad Singletons
```
AuthManager, GoogleAuthService, LoadoutManager, SoundManager,
SettingsManager, NetworkManager, UnityMainThreadDispatcher
```

---

## 14. LOADOUT MANAGER (LoadoutManager.cs)

### 14.1 Singleton lifecycle
- DontDestroyOnLoad
- Suscribe a `AuthManager.OnLoginSuccess` + `OnGuestReady` → `InitializeLoadoutFromAuth(UserData)`
- **CLAVE:** Solo escucha `OnLoginSuccess` y `OnGuestReady`. NO escucha eventos de Google directamente. Por eso `AuthManager` DEBE disparar `OnLoginSuccess` después de `OnGoogleLinkSuccess`/`OnGoogleLoginSuccess`.

### 14.2 LoadoutData
```csharp
userId, username, characterName, selectedCharacter
level, primaryWeapon, secondaryWeapon, knifeSkin
unlockedCharacters[], unlockedWeaponSkins
bluePoints, rivalCoins, sensitivity
```

### 14.3 API methods
- `UpdateCharacter(characterId, callback)` — Guest: local only + `SetGuestCharacter()`. Registered: `PUT /api/loadout`
- `UpdatePrimaryWeapon(weaponId, skinId, callback)` — con validación de skin unlocked
- `UpdateSecondaryWeapon(weaponId, skinId, callback)`
- `UpdateKnifeSkin(skinId, callback)`
- `UpdateSensitivity(value, callback)` — Guest: PlayerPrefs, Registered: backend
- `RefreshLoadout(callback)` — Re-fetches from backend
- `IsCharacterUnlocked(characterId)` — **case-insensitive** comparison
- `IsSkinUnlocked(weaponId, skinId)` — "default" always unlocked

### 14.4 API calls
- `PUT /api/loadout` con body `{ selectedCharacter, primaryWeapon, secondaryWeapon, knifeSkin, sensitivity }`
- Backend validates character unlocked (case-insensitive) + skin unlocked
- Returns updated loadout in response

### 14.5 Purchase flow (via ShopTabController)
1. ShopTabController selecciona item → POST `/loadout/purchase-skin`
2. Backend valida precio, ownership, balance → deduce currency + agrega skin
3. ShopTabController llama `LoadoutManager.RefreshLoadout()` (GET `/loadout`)
4. RefreshLoadout actualiza `currentLoadout` → ShopTabController repopula la tienda
5. El item comprado ahora muestra "OWNED" → no se puede re-comprar

---

## 15. BUGS RESUELTOS (HISTORIAL)

### 15.1 ParrelSync unique guest IDs
- **Problema:** Clones compartían el mismo guest identity
- **Fix:** `_keyPrefix = "clone_"` para PlayerPrefs keys en clones

### 15.2 Team assignment visual fix
- **Problema:** Team no se mostraba correctamente
- **Fix:** Corrección en la lógica de asignación visual

### 15.3 Duplicate session guard
- **Problema:** Se creaban sesiones duplicadas
- **Fix:** Guard en NetworkManager para prevenir múltiples sesiones

### 15.4 Countdown trigger fix
- **Problema:** Countdown no se iniciaba correctamente
- **Fix:** En GameStateManager

### 15.5 Ceremony overlay + loading screen
- **Problema:** Overlay y loading no funcionaban bien
- **Fix:** Timing y flujo corregidos

### 15.6 PredictedDead/PredictedHP system
- **Problema:** Multiple blood VFX on already-dead player, overkill
- **Fix:** Sistema de PredictedHP local para tracking antes de sync

### 15.7 Immunity bugs
- **Problema:** Inmunidad no funcionaba correctamente
- **Fix:** Layer InmunePlayer + temporal material swap + 3s timer

### 15.8 Landing sound/sway spam
- **Problema:** Sonido de landing y weapon sway se disparaban repeatedly
- **Fix:** Debounce + `DidJump` flag

### 15.9 Jump/land impulse
- **Problema:** Impulso visual desajustado
- **Fix:** WeaponSway impulse en tick correcto

### 15.10 3rd player immunity TPV visual
- **Problema:** El tercer jugador no veía la inmunidad en TPV
- **Fix:** Corrección en el sistema de inmunidad visual

### 15.11 Sensitivity ParrelSync save
- **Problema:** Sensitivity se compartía entre main y clones
- **Fix:** PlayerPrefs keys con K() prefix

### 15.12 Lock In button unreachable code ⭐
- **Problema:** El botón Lock In no hacía nada, sin output en consola
- **Causa raíz:** Cascading `if` statements sin braces en `CacheUIElements()`. Cuatro `if (x == null) // Debug.LogError(...)` se encadenaban, haciendo que el registro del handler del botón fuera **código inalcanzable**: `if(button==null) if(button!=null)` = imposible.
- **Fix:** Eliminados los `if` vacíos, dejando solo el registro del handler

### 15.13 Agents grid horizontal layout
- **Problema:** Grid de agentes vertical en vez de horizontal
- **Fix:** CSS `flex-direction: row` en container

### 15.14 Agent selection feedback
- **Problema:** No había feedback visual al seleccionar agente
- **Fix:** CSS `.agent-card-selected` con green outline (`rgb(0, 220, 100)`) + icon scale 1.08

### 15.15 Lock In save method
- **Problema:** `SaveAgentToLoadout` usaba `UpdateAgent()` en vez de `UpdateCharacter()`
- **Fix:** Cambiado a `UpdateCharacter()` que maneja guests y registered correctamente

### 15.16 Pato loadout not saving (case mismatch) ⭐
- **Problema:** Seleccionar Pato no se guardaba en backend
- **Causa raíz:** Backend almacenaba `['CRIMSON', 'PATO']` (uppercase), cliente enviaba `"pato"` (lowercase), comparación era case-sensitive → "Character locked"
- **Fix triple:**
  1. Backend `authService.js`: defaults cambiados a `['crimson', 'pato']`
  2. Client `LoadoutManager.IsCharacterUnlocked()`: comparación `OrdinalIgnoreCase`
  3. Backend `loadoutService.js`: validación `.some(c => c.toLowerCase() === lowerSelected)`

### 15.17 Tsunami Wave collision with all players
- **Problema:** La ola colisionaba con todos los jugadores (cada cliente tiene su player en layer Player)
- **Fix:** `IgnoreAllPlayerCollisions()` — llama `Physics.IgnoreCollision` entre todos los wave colliders y todos los CharacterControllers. Raycasts (balas) no se ven afectados → la ola sigue bloqueando disparos.

### 15.18 Google login no refrescaba loadout ⭐
- **Problema:** Después de Google Sign-In (link o login), el loadout no se actualizaba. La tienda mostraba items "locked" que el usuario ya tenía, y re-comprar daba error 400.
- **Causa raíz:** `GoogleLoginCoroutine` y `GoogleLinkCoroutine` disparaban `OnGoogleLoginSuccess`/`OnGoogleLinkSuccess` pero NO `OnLoginSuccess`. `LoadoutManager` solo escucha `OnLoginSuccess` + `OnGuestReady`, por lo que nunca se reinicializaba.
- **Fix:** Añadido `OnLoginSuccess?.Invoke(currentUser)` inmediatamente después de `OnGoogleLoginSuccess` y `OnGoogleLinkSuccess` en AuthManager.cs.

### 15.19 Rooms panel no se cerraba ⭐
- **Problema:** El botón de cerrar (`CloseRoomPanelButton`) del room panel existía en el UXML pero no hacía nada.
- **Causa raíz:** No se registraba ningún click handler para `CloseRoomPanelButton` en el C#.
- **Fix:** Registrado el handler en `RegisterEvents()` → añade clase "hidden" a `RoomPanelOverlay`.

### 15.20 Overlays detrás de GooglePromoPanel
- **Problema:** `RoomPanelOverlay` y `CreateRoomOverlay` aparecían detrás de la promo panel de Google porque estaban antes en el DOM.
- **Causa raíz:** `RoomPanelOverlay` estaba dentro del tab `LobbyContent` (z-order bajo), `CreateRoomOverlay` estaba antes de `GooglePromoPanel` en el DOM.
- **Fix:** Movidos TODOS los overlays al nivel de `MainContainer`, DESPUÉS de `GooglePromoPanel`. Orden final: GooglePromoPanel → RoomPanelOverlay → CreateRoomOverlay → SaveProgressOverlay → CharacterNameOverlay → LoginOverlay.

---

## 16. ARCHIVOS CREADOS Y MODIFICADOS EN SESIONES RECIENTES

### Scripts nuevos:
1. `Assets/Scripts/Weapons/RecoilPattern.cs` — SO con patron de recoil + counter-steer + movement mods
2. `Assets/Scripts/Abilities/TsunamiWaveAbilityConfig.cs` — SO config para ola de Pato
3. `Assets/Scripts/Abilities/WaterSuperJumpAbilityConfig.cs` — SO config para super jump de Pato
4. `Assets/Scripts/Abilities/TsunamiWave.cs` — MonoBehaviour de la ola (rise, move, ride, ignore collisions)
5. `Assets/Scripts/Auth/GoogleAuthService.cs` — Singleton wrapper para Google Sign-In SDK
6. `Assets/Scripts/Data/ShopItemDefinition.cs` — Registro estático de items comprables
7. `Assets/Scripts/UI/ShopTabController.cs` — Controlador de la pestaña Shop (cards, buy button, purchase API)

### Scripts modificados:
1. `Assets/Scripts/Weapons/WeaponRecoil.cs` — Reescrito completamente con pattern engine + counter-steer
2. `Assets/Scripts/Weapons/WeaponConfig.cs` — Añadido campo `recoilPattern`
3. `Assets/Scripts/Weapons/FireWeapon.cs` — `weaponRecoil.ResetPattern()` on reload + Water layer en hitMask
4. `Assets/Scripts/Data/AgentDefinition.cs` — Añadido Pato al registro
5. `Assets/Scripts/UI/AgentsTabController.cs` — Fix cascading if bug + UpdateCharacter + CSS classes
6. `Assets/Scripts/Abilities/AbilitySystem.cs` — Extendido para soportar Pato (AbilitySet enum, switch handlers, tsunami RPC, super jump)
7. `Assets/Scripts/Game/PlayerController.cs` — Añadido `SetVelocityY(float)`
8. `Assets/Scripts/Managers/LoadoutManager.cs` — `IsCharacterUnlocked` case-insensitive, `IsSkinUnlocked`, `RefreshLoadout(callback)`
9. `Assets/Scripts/Auth/AuthManager.cs` — Google Sign-In (GoogleLink, GoogleLogin), `OnLoginSuccess` disparado después de eventos Google, `ClearSavedAuth()`, Google events
10. `Assets/Scripts/UI/LobbyUIController.cs` — Google promo panel, Google Sign-In flows, room panel overlay, CloseRoomPanelButton handler, overlays DOM management, OnLogoutPerformed handler
11. `Assets/Scripts/UI/SettingsUIController.cs` — `OnLogoutPerformed` event, logout calls `AuthManager.Logout()`
12. `Assets/UI/Lobby/LobbyScreen.uxml` — RoomPanelOverlay modal, CreateRoomOverlay con "CUSTOM GAME", GooglePromoPanel, SaveProgressOverlay con imágenes, CharacterNameOverlay, LoginOverlay, DOM reordenado
13. `Assets/UI/Lobby/LobbyScreen.uss` — Shop card/buy button styles (64px icons, 360×64 buy), room-popup-overlay/panel (780×530, purple theme), google promo icon (64px), google-signin-image
14. `Backend/src/services/authService.js` — Defaults lowercase, Pato unlocked, `googleLink()`, `googleLogin()`, `verifyGoogleIdToken()`
15. `Backend/src/services/loadoutService.js` — Validación case-insensitive, `purchaseSkin()` function
16. `Backend/src/routes/authRoutes.js` — Endpoints `/google-link`, `/google-login`
17. `Backend/src/routes/loadoutRoutes.js` — Endpoint `/purchase-skin`
18. `Backend/src/database/db.js` — Columna `google_id VARCHAR(128) UNIQUE`, `blue_points DEFAULT 1000`, migrations

### Assets de UI:
- `Assets/Resources/Buttons/SignUpButton.png` — Imagen botón Google Sign Up (179×40px)
- `Assets/Resources/Buttons/SignInButton.png` — Imagen botón Google Sign In (179×40px)
- `Assets/Resources/Icons/RivalEssenceIcon` — Icono moneda Rival Essence
- `Assets/Resources/Icons/RivalPointsIcon` — Icono moneda Rival Points
- `Assets/Resources/Icons/TalonSkullIcon` — Icono skin Talon-Skull

---

## 17. SETUP PENDIENTE EN UNITY EDITOR

Para que las abilities de Pato funcionen en runtime, se necesita completar en el Editor:

1. **TsunamiVFX Prefab:**
   - Añadir componente `TsunamiWave` al root del prefab
   - Los hijos WaterCollider/WaterCollider(1)/WaterCollider(2) deben estar en layer **Water**
   - Los colliders deben tener `isTrigger = OFF` (solid)

2. **ScriptableObject Assets (via Create menu):**
   - `TsunamiWaveAbilityConfig` asset → asignar wavePrefab (TsunamiVFX), spawnSound, icon, cooldownSeconds
   - `WaterSuperJumpAbilityConfig` asset → asignar jumpSound, icon, cooldownSeconds

3. **pato.asset (CharacterConfig):**
   - `ability1` → TsunamiWaveAbilityConfig asset
   - `ability2` → WaterSuperJumpAbilityConfig asset
   - `characterId` → "pato"
   - Meshes: tpvMesh, armsMesh, materials assigned

4. **Unity Collision Matrix** (Edit → Project Settings → Physics):
   - Water layer: solo colisiona con Player layer

---

## 18. CONVENCIONES DE CÓDIGO

### Namespaces
```
ArtisansGuns.Abilities
ArtisansGuns.Auth
ArtisansGuns.Characters
ArtisansGuns.Data
ArtisansGuns.Game
ArtisansGuns.Loading
ArtisansGuns.Managers
ArtisansGuns.Networking
ArtisansGuns.UI
ArtisansGuns.Weapons
```

### Patrones recurrentes
- **Singleton:** `Instance` estático + `DontDestroyOnLoad` (AuthManager, GoogleAuthService, LoadoutManager, NetworkManager, SoundManager, SettingsManager, MobileControlsController, GameStateManager, UnityMainThreadDispatcher)
- **RPC pattern:** `[Rpc(RpcSources.InputAuthority, RpcTargets.All)]` + `Object.HasInputAuthority` guard
- **Resource loading fallback:** `Resources.Load($"Characters/{lower}")` → si null → capitalizar → intentar de nuevo
- **Ability spawn:** `Instantiate` dentro de RPC (NO `Runner.Spawn`) — cada cliente crea su propia instancia local
- **Guard pattern:** `if (!Object.HasInputAuthority) return;` al inicio de métodos solo-local
- **Static events:** `MobileControlsController.OnAbility1 += handler` (subscribe in Spawned, unsubscribe in Despawned)
- **ParrelSync:** Keys prefixed with `K(key)` para aislar clones
- **UIToolkit button rebuild:** `button.text = ""; button.Clear();` + añadir VisualElements hijos (para botones con iconos)
- **Event chain:** `OnGoogleXxxSuccess` → `OnLoginSuccess` (siempre disparar ambos para que LoadoutManager se reinicialice)
- **Overlay visibility:** Usar `.AddToClassList("hidden")` / `.RemoveFromClassList("hidden")` para toggle de overlays UIToolkit

### Case conventions
- Agent IDs: **lowercase** everywhere (`"crimson"`, `"pato"`)
- Display names: **UPPERCASE** (`"CRIMSON"`, `"PATO"`)
- Backend storage: debe ser **lowercase** (fijado en sesión reciente)
- Comparaciones: siempre **case-insensitive** (`OrdinalIgnoreCase` en C#, `.toLowerCase()` en JS)

---

## 19. FLUJO DE JUEGO COMPLETO

```
LoginScene
  ↓ AuthManager: guest auto → or login/register
  ↓ LoadoutManager receives UserData, initializes loadout
  ↓
LobbyScene
  ↓ NetworkManager creates/joins Fusion session
  ↓ PlayerNetworkData spawned, sets Username/Team/Agent/Weapons
  ↓ LobbyUIController shows player list, agent tab, weapons tab, shop tab
  ↓ [Optional] Guest ve GooglePromoPanel → "GET STARTED" → Google Sign-In
  ↓   → Link: GoogleAuthService.SignIn() → AuthManager.GoogleLink() → OnGoogleLinkSuccess + OnLoginSuccess
  ↓   → Login: GoogleAuthService.SignIn() → AuthManager.GoogleLogin() → OnGoogleLoginSuccess + OnLoginSuccess
  ↓   → LoadoutManager reinitializes from OnLoginSuccess → currency + skins updated
  ↓ Player selects agent (AgentsTabController) → Lock In → UpdateCharacter
  ↓ Player selects weapons (WeaponsTabController) → save
  ↓ Player compra skins (ShopTabController) → POST /purchase-skin → RefreshLoadout
  ↓ Player creates room ("CREATE / JOIN") o joins public room ("ROOMS")
  ↓ Host presses Start → NetworkManager loads GameScene for all
  ↓
GameScene
  ↓ MapSpawnManager spawns player at team spawn point
  ↓ PlayerSetup: spawns weapons (primary, secondary, knife)
  ↓ CharacterSetupHandler: applies meshes + initializes AbilitySystem
  ↓ TeamLayerAssigner: sets Enemy/Teammate layers for remote players
  ↓ GameStateManager.StartCountdown() → 3-2-1 ceremony
  ↓ InputFrozen = true during countdown → InputFrozen = false → FIGHT
  ↓ Players: move (CharacterController), shoot (raycast), abilities (RPC)
  ↓ Death: RPC_TakeDamage → HP drops → RPC_Die → death overlay → respawn
  ↓ Respawn: 3s cooldown → new spawn point → 3s immunity
  ↓ [Match loop continues until host ends / disconnect]
```

---

## 20. NOTAS TÉCNICAS IMPORTANTES

1. **Shared Mode quirk:** `HasStateAuthority` es solo para el host en Shared Mode. Usar `HasInputAuthority` para "soy el dueño de este player".

2. **PlayerNetworkData.PlayerCache:** Dictionary estático que sobrevive scene changes — es la fuente fiable de datos de jugadores.

3. **Gravity = -33f:** Muy fuerte intencionalmente para feel responsive en móvil.

4. **MAX_HP = 150f:** No 100 — customizado para el balance del juego.

5. **RESPAWN_COOLDOWN = 2.0f** en PlayerNetworkData para prevenir rapid respawn loops.

6. **Google Sign-In:** Implementado y funcional. `GoogleAuthService.cs` (DontDestroyOnLoad singleton) wrappea Google Sign-In SDK 1.0.4. En Editor usa token mock `EDITOR_TEST_*` que el backend acepta en dev mode. Web Client ID: `329775748159-oj54pn4q1l2e13khrkfk76105117q18n.apps.googleusercontent.com`.

7. **Joystick Pack:** Asset de terceros (`Assets/Joystick Pack/`) para input de movimiento.

8. **VFX Graph:** Usado para efectos de muerte (`deathVFXPrefab` en CharacterConfig).

9. **KaTeX compatibility:** El recoil pattern usa Vector2 — X=horizontal, Y=vertical (positivo = arriba/derecha).

10. **Database migrations:** `db.js` tiene `migrateExistingUsers()` que se ejecuta en cada server start — añade columnas nuevas sin romper users existentes. Migra: `google_id`, `blue_points DEFAULT 1000`, `rival_coins`.

11. **Currency:** `blue_points` = Rival Essence (default 1000 para nuevos usuarios), `rival_coins` = Rival Points (default 0). Se muestran en el header del lobby con iconos.

12. **Shop UI pattern:** Los botones de compra con iconos de moneda se construyen dinámicamente con `button.Clear()` + VisualElements hijos (Label + Image). El icono siempre va a la DERECHA del precio.

13. **UIToolkit overlays:** Todos los overlays modales están al nivel de `MainContainer` (NO dentro de tabs). El orden en el DOM determina el z-order. Usar class `.hidden` (display: none) para toggle.

14. **Room panel:** Ahora es modal (full-screen dark backdrop `RoomPanelOverlay` → panel interior `RoomPanel` 780×530px). El botón que antes decía "PRIVATE ROOM" ahora dice "CREATE / JOIN" y el overlay se llama "CUSTOM GAME" con secciones separadas para crear y unirse por código.

---

*Documento actualizado. Cubre todo el conocimiento acumulado hasta las sesiones de Google Sign-In, tienda de skins, rediseño del room panel, y corrección de bugs de loadout/overlays.*
