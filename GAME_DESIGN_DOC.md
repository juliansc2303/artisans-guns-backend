# ARTISANS GUNS - Game Design Document

## CONCEPTO CORE

**Genre**: Hero FPS (First Person Shooter)
**Theme**: Aves de combate con habilidades únicas basadas en sus especies
**Inspiración**: Valorant (simplificado) + Overwatch (peso en personajes)

---

## PILARES DE DISEÑO

### 1. IDENTIDAD POR PERSONAJE
- Cada ave tiene personalidad única basada en su especie
- Arma signature (única por personaje)
- Estilo de movilidad diferenciado
- Habilidades coherentes con la naturaleza del ave

### 2. PESO Y TACTO
- Cada personaje debe sentirse distinto al jugarlo
- La velocidad, el peso, el estilo de combate son diferentes
- No es solo cosmetico - cambia la experiencia de juego

### 3. ESCALABILIDAD
- Sistema modular para agregar personajes
- Pipeline claro de producción (ya establecido)
- Arquitectura que soporte crecimiento

---

## 🔴 CRIMSON - MVP CHARACTER (ÚNICO EN FASE 1)

**Especie**: Cardenal norteño (Northern Cardinal)  
**Rol**: Duelista agresivo / Hit-and-Run specialist  
**Fantasía**: Ave territorial, explosiva, ágil en movimiento de ráfagas

### **Visual & Concepto:**
- Pájaro humanizado con cresta roja prominente
- Máscara oscura característica (naturaleza del cardenal)
- Pico visible y definido
- Armadura militar accesible
- Movimiento ágil, no pesado

### **TALON BURST (Arma Signature)**

**Especificaciones:**
- Rifle de 3 disparos por ráfaga
- **Sistema: HitScan (raycast)**
- **Recoil VERTICAL:** Controlable con stick/mouse
- **Recoil HORIZONTAL:** Leve, predecible
- **SIN SPREAD:** Las balas SIEMPRE van donde apuntas
- Daño base: 25 por disparo (75 por ráfaga)
- Cadencia: 0.1s entre ráfagas (10 ráfagas/segundo)
- **Sin recarga:** Disparo infinito

**Filosofía:**
- Equilibrio viene de MOVIMIENTO + TIMING, no de RNG spread
- Precisión pura requiere control de recoil + posicionamiento
- Difícil pero justo: no hay suerte, solo skill

---

### **AERIAL BURST (Movimiento - Q)**

**Especificaciones:**
- Presiona Q → ejecuta dash explosivo diagonal
- Distancia: 5-6 metros
- Duración: ~0.2 segundos (explosivo, instantáneo)
- **Cooldown: 3 segundos** (siempre disponible)
- Puedes hacer **2 bursts encadenados** mientras estés en aire
- Al aterrizar, reinicia contador de aire
- Dirección: la que estés apuntando en ese momento

**Profundidad:**
- **Novato:** Usa para moverse, es un dash
- **Intermedio:** Coordina timing con Talon Burst (dispara-burst-dispara)
- **Experto:** Rhythm puro, timing de bursts cada 3s, dodge patterns, air control

**Essence:** Vuelo explosivo en ráfagas como cardenal real - cambios de dirección abruptos, movimiento con propósito

---

### **Stats Base:**
- HP: 100
- Velocidad base: 5.5 m/s
- Jump height: Estándar
- Se modifican por ECONOMY SYSTEM (ver sección)

---

### **Audio Signature:**
- Pasos agresivos/pesados
- Sonido de Talon Burst distintivo
- Grunt vocal al hacer Aerial Burst
- Enemigos deben ESCUCHAR que CRIMSON viene

---

### 💗 VIBE (Ave del Paraíso - Soporte Tecnológico)
**STATUS:** Fase 2 - No incluido en MVP

---

### 🔵 SIGHT (Águila - Tirador de Precisión)
**STATUS:** Fase 2 - No incluido en MVP

---

### 🦆 PATO (Duck - Tanque Adaptable)
**STATUS:** Fase 2 - No incluido en MVP

> **MVP SCOPE:** Solo CRIMSON implementado. Otros personajes siguen arquitectura idéntica: Arma signature + Movimiento unique + Stats afectados por Economy System

---

## ARQUITECTURA DE CÓDIGO - UNITY C#

### ESTRUCTURA MODULAR

```
Assets/
├── _Project/
│   ├── Characters/
│   │   ├── Base/
│   │   │   ├── CharacterBase.cs          # Clase base abstracta
│   │   │   ├── ICharacterStats.cs        # Interface de stats
│   │   │   ├── IAbility.cs               # Interface de habilidades
│   │   │   └── CharacterAnimController.cs
│   │   │
│   │   ├── Data/
│   │   │   └── CharacterDataSO.cs        # ScriptableObject config
│   │   │
│   │   └── Specific/
│   │       ├── Crimson/
│   │       │   ├── CrimsonCharacter.cs
│   │       │   ├── CrimsonAbilities.cs
│   │       │   ├── Arms_Crimson_FPV/     # Brazos FPV
│   │       │   └── Model_Crimson_TPV/    # Modelo completo
│   │       │
│   │       ├── Vibe/
│   │       ├── Sight/
│   │       └── Pato/
│   │
│   ├── Weapons/
│   │   ├── Base/
│   │   │   ├── WeaponBase.cs
│   │   │   ├── IWeapon.cs
│   │   │   └── WeaponDataSO.cs
│   │   │
│   │   └── Specific/
│   │       ├── TalonBurst/               # Arma de Crimson
│   │       ├── HarmonyBeam/              # Arma de Vibe
│   │       ├── EagleEyeDMR/              # Arma de Sight
│   │       └── QuackCannon/              # Arma de Pato
│   │
│   ├── Abilities/
│   │   ├── AbilitySystem.cs              # Sistema central
│   │   ├── AbilityBase.cs                # Clase base
│   │   └── Implementations/
│   │       ├── DashAbility.cs
│   │       ├── MarkAbility.cs
│   │       ├── BuffAbility.cs
│   │       └── ShieldAbility.cs
│   │
│   ├── Core/
│   │   ├── GameManager.cs
│   │   ├── InputManager.cs               # Input System integration
│   │   ├── UIManager.cs
│   │   └── MatchManager.cs
│   │
│   ├── FPV/
│   │   ├── FPVController.cs              # Control primera persona
│   │   ├── FPVAnimationRig.cs            # Animation Rigging setup
│   │   ├── ArmsController.cs             # Control de brazos
│   │   └── IKTargetsManager.cs           # Two Bone IK manager
│   │
│   └── GameModes/
│       ├── GameModeBase.cs
│       ├── DeathMatch.cs
│       ├── TeamDeathMatch.cs
│       └── Objective.cs
│
└── Scenes/
    ├── MainMenu.unity
    ├── CharacterSelect.unity
    └── Maps/
        └── TestMap_01.unity
```

---

## SISTEMA DE HABILIDADES (Arquitectura)

### Patrón de Diseño: Component-Based

```csharp
// Cada habilidad es un componente independiente
// Se añade dinámicamente según el personaje

public abstract class AbilityBase : MonoBehaviour
{
    public string abilityName;
    public float cooldown;
    public KeyCode keyBind;
    
    protected float lastUsedTime;
    protected CharacterBase character;
    
    public abstract void Activate();
    public virtual bool CanActivate() 
    {
        return Time.time >= lastUsedTime + cooldown;
    }
}

// Ejemplo: Dash de Crimson
public class DashAbility : AbilityBase
{
    public float dashDistance = 8f;
    public float dashDuration = 0.3f;
    
    public override void Activate()
    {
        if (!CanActivate()) return;
        
        StartCoroutine(DashCoroutine());
        lastUsedTime = Time.time;
    }
    
    private IEnumerator DashCoroutine()
    {
        // Lógica del dash
    }
}
```

---

---

## MODOS DE JUEGO

### 🎯 TERRITORIAL CONTROL - MVP Mode (ÚNICO EN FASE 1)

**Formato:** 5v5, Best of 5 rondas  
**Duración estimada:** 15-20 minutos por partida

#### **Cómo funciona UNA RONDA:**

**Setup:**
```
Team Rojo [Spawn]      Team Azul [Spawn]
    ↓                        ↓
    ────────────────────────
    │     ZONA CENTRAL      │  ← Neutral al inicio
    │   (Control Point)      │
    ────────────────────────
```

**Objetivo:**
- Primer equipo en **estar 9 segundos en la zona ENEMIGA = GANA RONDA**
- Zona enemiga de Rojo = zona Azul
- Zona enemiga de Azul = zona Rojo

**Mecánica:**
1. Jugador entra a zona enemiga
2. Contador empieza: 1... 2... 3...
3. Si enemigos están presentes = contador se pausa
4. Si enemigos desaparecen = contador continúa
5. Llega a 9 segundos = su equipo gana ronda

**Si mueres en zona:**
- Respawneas en 9 segundos (castigo = mismo tiempo que win condition)
- Tu equipo pierde el control de zona mientras estés dead

**Fin de ronda:**
- Si un equipo logra 9 segundos = gana ronda (incluso si es antes de 90s)
- Si llega 90s sin nadie con 9s = gana quien tenga más KILLS esa ronda
- Si empate de kills también = sudden death (contador continúa)

**Duración:** 90 segundos máximo

---

#### **Cómo se gana la PARTIDA:**

```
Best of 5 rondas (primer equipo en 3 puntos gana)

Ronda 1: Team Rojo gana
Ronda 2: Team Azul gana
Ronda 3: Team Rojo gana
Ronda 4: Team Rojo gana
→ TEAM ROJO GANA PARTIDA (3-1)
```

---

### **ECONOMY SYSTEM - Crecimiento por Ronda**

**Puntos por ronda:**
- **+1 punto** por ganar ronda (completar 9s en zona enemiga)
- **+1 punto** por cada KILL durante esa ronda
- Puntos se acumulan entre rondas

**Cómo gastar puntos:**
- Cada ronda, antes de que empiece, seleccionas dónde invertir tu punto acumulado
- 3 opciones:

```
[+] DAMAGE (Talon Burst)
    └─ +5% daño por punto
    └─ Ej: +3 puntos = +15% daño total = 28.75 daño/disparo (de 25)

[+] FIRERATE (Talon Burst)
    └─ +10% cadencia por punto
    └─ Ej: +2 puntos = 20% más rápido disparar

[+] MOVEMENT (Aerial Burst + velocidad base)
    └─ +8% velocidad total por punto
    └─ Ej: +4 puntos = 32% más rápido movimiento y bursts
```

**Estrategia:**
- Team puede coordinarse o jugar independiente
- Cada jugador elige su build
- Snowball es real: si vas 3-0, tienes 3 puntos = más fuerte

---

### **Build Examples:**

**"Speedrunner" (Full Movement):**
```
Ronda 1: +1 Movement
Ronda 2: +1 Movement
Ronda 3: +1 Movement
Total: +3 Movement = 24% más rápido
→ Objetivo: Llegar zona, completar 9s, escapar
```

**"One-Tap God" (Full Damage):**
```
Ronda 1: +1 Damage
Ronda 2: +1 Damage
Ronda 3: +1 Damage
Total: +3 Damage = 15% más fuerte (28.75 daño/disparo)
→ Objetivo: Duelos en zona, kills rápidas
```

**"Balanced" (Mixed):**
```
Ronda 1: +1 Damage
Ronda 2: +1 Movement
Ronda 3: +1 Firerate
Total: Versatilidad, sin especialidad
→ Objetivo: Adaptarse a meta
```

---

### **META PROGRESSION (Ejemplo de partida):**

```
RONDA 1 - EARLY (Todos débiles):
- Rojo elige +1 Movement
- Azul elige +1 Damage
- Resultado: Rojo llega zona antes, Azul pega más fuerte
- Outcome: Rojo llega en 6s, Azul mata a Rojo en 8s, Azul completa 9s
- AZUL GANA (1-0)

RONDA 2 - EARLY CONT:
- Rojo ahora elige +1 Damage (counter a Azul)
- Azul elige +1 Movement (respeta velocidad de Rojo)
- Resultado: Más equilibrado
- Outcome: Rojo y Azul llegan mismo tiempo, batalla
- Azul tiene 2 puntos (damage+movement) = mejor stats
- AZUL GANA (2-0)

RONDA 3 - AZUL EN VENTAJA:
- Rojo está 0-2, necesita ganar
- Rojo elige +1 Firerate (ataque rápido)
- Azul elige +1 Damage (ya confiado)
- Rojo tiene: +2 Damage, +1 Firerate = spray rápido y preciso
- Azul tiene: +2 Damage, +2 Movement = tanque rápido
- Resultado: Rojo mata a Azul con spray perfecto en zona
- ROJO GANA (2-1)

RONDA 4 - AZUL EN LA CUERDA FLOJA:
- Azul necesita ganar para ir 3-1
- Rojo necesita empatar para ir 2-2
- Rojo elige +1 Movement (4 puntos total en builds variados)
- Azul elige +1 Firerate (ataque rápido, presión)
- Azul ahora: +2 Damage, +2 Movement, +1 Firerate
- Rojo: +2 Damage, +2 Movement, +1 Firerate
- EMPATE DE BUILDS
- Outcome: Pura skill, timing perfecto, ROJO mata primero
- ROJO GANA (2-2)

RONDA 5 - FINAL (SUDDEN DEATH):
- Ganador = a partida
- Rojo: +3 Damage, +2 Movement, +0 Firerate (sniper build)
- Azul: +2 Damage, +3 Movement, +1 Firerate (balanced)
- Rojo es letal pero lento
- Azul es versátil
- Azul llega zona primero, Rojo reacciona lentamente
- AZUL COMPLETA 9s y GANA PARTIDA (3-2)
```

---

### **Por qué Territorial funciona con Economy:**

✅ **No hay recarga:** Disparas infinito, si no hay recoil skill = ganador verdadero  
✅ **Builds importan:** Full damage vs full speed = estrategia diferente  
✅ **Snowball real:** Kills dan puntos = equipo adelantado es más fuerte  
✅ **Counterplay:** Si ves Speedrunner en zona, puedes counter con Damage  
✅ **Presión temporal:** 90s = rondas no mueren, urgencia  
✅ **Early/Mid/Late:** Distintos builds dominan en fases diferentes  

---

## FUTURO: Otros Modos (No MVP)

### **Capture the Flag (Phase 2+)**
- Similar a Territorial pero con bandera
- Portador vulnerable
- Requiere tank/soporte
- Mejor cuando hay 3+ personajes

### **Wave Defense (Phase 3+)**
- PvE o PvP
- Rondas cortas, enemigos spawneán más fuerte
- Cooperativo



---

---

## GAME FLOW (MVP)

### **User Journey:**

```
1. LAUNCH GAME
   └─ Splash screen (ArtesanoGames)

2. AUTH SCREEN
   ├─ Login con Google (OAuth2 via Firebase Auth)
   ├─ ó Create account (si es primera vez)
   └─ Google maneja: signup, password recovery, security

3. MAIN MENU
   ├─ Profile (shows username, stats)
   ├─ CHARACTER SELECT
   │  └─ [CRIMSON] ← Solo personaje MVP
   │     ├─ Thumbnail
   │     ├─ Stats: HP, Movimiento, Arma info
   │     └─ [SELECT] button
   ├─ SETTINGS (audio, graphics, controls)
   └─ [FIND MATCH] button

4. CHARACTER SELECTED
   └─ Screen shows "CRIMSON Selected"
   └─ [FIND MATCH] available

5. FIND MATCH (Matchmaking)
   ├─ "Finding match..." screen
   ├─ Waiting for 10 players (5v5)
   └─ Estimated time display

6. MATCH FOUND
   ├─ Loading screen
   ├─ Shows 5 Rojos vs 5 Azules
   ├─ CRIMSON is always 5 copies (no mirrors importa)
   └─ Wait 3s, game starts

7. PREGAME (Antes de Ronda 1)
   ├─ Economy screen: "Elige dónde gastar tu primer punto"
   ├─ 3 options: [+Damage] [+Firerate] [+Movement]
   ├─ 10 segundos para elegir
   └─ Pantalla se cierra, spawn en mapa

8. GAME - RONDA 1
   ├─ Spawn en base
   ├─ Objetivo visible en UI: "Llega a ZONA ENEMIGA"
   ├─ Zona parpadeante en mapa
   ├─ 90 segundos de tiempo ronda
   ├─ HUD: Timer, health, equipo vivo, contador zona
   └─ GAMEPLAY

9. END RONDA
   ├─ "AZUL GANA RONDA" (ó "ROJO GANA")
   ├─ Puntos ganados (ronda + kills)
   ├─ Tabla de stats: kills, deaths, accuracy
   └─ 10 segundos descanso

10. PREGAME RONDA 2
    └─ Vuelves a elegir dónde gastar punto (Damage/Firerate/Movement)

11. [Repite rondas 3, 4, 5 hasta Best of 5]

12. POST MATCH
    ├─ "TEAM ROJO GANA PARTIDA 3-2"
    ├─ Premios: puntos, skin currency (futuro)
    ├─ Leaderboard personal update
    └─ [NEXT MATCH] ó [MAIN MENU]
```

---

### **UI Elements MVP:**

**HUD In-Game:**
```
┌──────────────────────────────────────┐
│ ⚪ HP: 100/100      [CRIMSON]        │
│ 📍 Zone: 4/9s      TEAM: 2/5 alive  │
│ 🔫 Talon Burst     Enemy: 3/5 alive │
│                                       │
│           [Zone] 🎯 30m away         │
│                                       │
│ ⏱️  RONDA 1: 45/90s   [SCORE: 1-0]   │
│                                       │
│ Q (3.2s) - Aerial Burst available    │
└──────────────────────────────────────┘
```

**Economy Screen (Pre-Ronda):**
```
RONDA 2 - ELIGE TU BUILD
Puntos acumulados: 2 (1 por ganar ronda + 1 por kill)

[+] DAMAGE         [+] FIRERATE       [+] MOVEMENT
    +5% daño            +10% cadencia      +8% velocidad
    
    Current: 25dmg      Current: 10 rf     Current: 5.5 m/s
    New: 26.25dmg       New: 11 rf         New: 5.94 m/s
    
    [SELECT]           [SELECT]           [SELECT]
```

---

## GAME FLOW - TECHNICAL

### **Network Flow:**

```
Client                          Firebase Auth          Backend (Node.js)       FishNet Server
  │                                 │                        │                      │
  ├─ Login w/ Google ─────────────→ ├─ Verify token         │                      │
  │                                 ├─ Generate JWT ──────→ │                      │
  │                                 │                  ├─ Create session         │
  │                                 │← JWT token ───────────│                      │
  │← Auth success ────────────────→ │                       │                      │
  │                                 │                       │                      │
  ├─ Select CRIMSON               │                       │                      │
  ├─ Click "FIND MATCH" ───────────────────────────────→ │                      │
  │                                                  ├─ Query available          │
  │                                                  │ game servers              │
  │                                                  │                           │
  │     [Waiting... finding 10 players...]          │                      ├─ Check CCU
  │                                                  │   ┌──────────────────────┤
  │                                                  │   │ If 10/10 → start game│
  │                                                  ├─→ │ Assign port          │
  │                                                  │   │ Generate match ID    │
  │                                                  │   └──────────────────────┤
  │← Match found, connect to server IP:port ────────────┼──→ [GameServer_1]     │
  │                                                      │                      │
  ├─ Connect FishNet ────────────────────────────────────────────────────────→ │
  │ (UNet transport, auto-replication)                                         │
  │                                                                              │
  ├─ Spawn CRIMSON ────────────────────────────────────────────────────────→ │
  │ (Position, stats, build selection)                                        │
  │                                                                              │
  ├─ Input (move, shoot, aerial burst) ────────────────────────────────────→ │
  │                                                                              │
  ├─← Networked player updates ────────────────────────────────────────────←│
  │ (other players, zone control, kills)                                       │
  │                                                                              │
  └─ [End of ronda / End of match] ────────────────────────────────────────→ │
    Backend updates stats, disconnects                                         │
```



---

## TECH STACK - MVP

### **Frontend (Unity Mobile)**

**Platform:**
- Android (APK distribution via artisanal channels)
- iOS (future, TestFlight para testing)

**Target Device:**
- Tablets & phones (4.7" - 12.9")
- Min specs: 2GB RAM, Snapdragon 670 (Android)

**Framework:**
- Unity 2022+ (LTS)
- Input System (mobile-friendly)
- URP (rendering performance)

**Key Libraries:**
- Firebase SDK (Auth)
- FishNet (networking)
- TextMesh Pro (UI)

---

### **Backend (Self-Hosted)**

**Language:** Node.js + Express  
**Port:** 3000 (backend), 7777+ (game servers)

**Responsibilities:**
- OAuth2 flow with Google/Firebase
- Session management
- Matchmaking service
- Elo/rank system (future)
- Database (user profiles, stats)
- Game server orchestration

**Database:**
- MongoDB (user data, match history)
- Redis (session cache, matchmaking queue)

---

### **Game Server (Self-Hosted)**

**Technology:** FishNet + custom server logic

**Per Server:**
- Handles 1 game session (5v5 Territorial)
- Auto-starts when 10 players ready
- Auto-stops when match ends
- Reports result to backend

**Infrastructure:**
- VPS (Digital Ocean / Linode)
- $5-20/month per server instance
- Auto-scaling based on queue length
- Spawn new servers as needed

---

### **Authentication System**

**Flow:**
1. Player taps "Login with Google"
2. OAuth2 redirect (Google Play Services on Android)
3. Firebase Auth handles token generation
4. Backend receives Firebase ID token
5. Backend creates JWT session token
6. Client connects to backend with JWT

**Advantages:**
- No password recovery (Google handles)
- One account per Google account (no spam)
- Feels premium
- Secure (Firebase + backend validation)

**No PlayStore/AppStore dependency:**
- Direct APK distribution
- Google Auth still works outside stores
- Future: sideload or web-based distribution

---

### **Networking Architecture**

**Protocol:** FishNet (built on Netcode for GameObjects)

**Why FishNet for MVP:**
✅ Better lag compensation than Photon  
✅ Cheaper than Photon ($0 vs CCU-based)  
✅ Scalable (self-hosted)  
✅ C# integration (Unity-native)  
✅ HitScan friendly (server validates)  

**Network Replication (MVP):**
- Player position/rotation
- Shooting (RPC call → server validates hit)
- Health/death
- Zone control timer
- Score/kills

**Lag Compensation:**
- Server-authoritative shooting
- Rewind hitboxes (FishNet feature)
- Interpolation on clients

---

### **Server Architecture**

```
Internet
    │
    ├─ Game Client (mobile)
    │
    ├─────────────────────────────────┐
    │                                 │
    │  Artisano Games Backend         │
    │  (Node.js:3000)                 │
    │                                 │
    │  ├─ Auth endpoints              │
    │  ├─ Matchmaking service         │
    │  ├─ Session manager             │
    │  ├─ Database handler            │
    │  └─ Server orchestrator          │
    │                                 │
    ├─────────────────────────────────┤
         │
         ├─ Game Server 1 (FishNet:7777)
         │  └─ Handles match 1 (5v5)
         │
         ├─ Game Server 2 (FishNet:7778)
         │  └─ Handles match 2 (5v5)
         │
         ├─ Game Server N (FishNet:777X)
         │  └─ Handles match N (5v5)
         │
         └─ Database
            ├─ MongoDB (user data)
            └─ Redis (cache)
```

---

### **Cost Estimation (MVP + 50 CCU)**

**Monthly:**
- Backend VPS: $10 (1GB RAM, 25GB storage)
- Game Servers: $40 (4x servers @ $10 each)
- Database: $0 (MongoDB free tier, Redis local cache)
- Domain: $12
- **Total: ~$62/month**

**Per Match Cost:** ~0.003¢ (negligible)

---

### **Deployment & Updates**

**Client (APK):**
- Manual versioning
- Distribute via direct link (Discord, website)
- User downloads & installs manually
- Future: auto-patcher (check version on startup)

**Backend:**
- Git repo (private)
- Deploy via SSH or Docker
- Zero-downtime deploys (new servers spin up before old shut down)
- Database migrations (manual for now)

**Game Servers:**
- Spawned automatically by orchestrator
- Killed after match ends
- Self-healing (if crash, orchestrator respawns)

---

## MVP ROADMAP

### **Phase 1: MVP Launch (8-12 weeks)**

**Week 1-2: Core Setup**
- [ ] Auth system (Firebase + Node.js backend)
- [ ] Matchmaking service (queue, lobby)
- [ ] Database schema (users, matches)

**Week 3-4: Game Core**
- [ ] CRIMSON character controller
- [ ] Talon Burst weapon (hitscan + recoil)
- [ ] Aerial Burst movement
- [ ] Health/death system

**Week 5-6: Territorial Control**
- [ ] Zone implementation (UI, timer, control)
- [ ] Spawning/respawning
- [ ] Round system (90s, best of 5)
- [ ] Economy system (point selection)

**Week 7-8: Networking**
- [ ] FishNet integration
- [ ] Player replication
- [ ] Shooting sync
- [ ] Zone sync

**Week 9-10: UI/Polish**
- [ ] Main menu
- [ ] Character select
- [ ] HUD (zone, timer, team)
- [ ] Economy screen
- [ ] Post-match screen

**Week 11-12: Testing & Deployment**
- [ ] Closed alpha testing (friends)
- [ ] Bug fixes
- [ ] Backend deployment
- [ ] APK distribution setup

**Launch:** Exclusive release (Discord/friends)

---

### **Phase 2: Expand Roster (4-6 weeks)**
- [ ] VIBE character + Hover Float
- [ ] SIGHT character + Thermal Glide
- [ ] PATO character + Waddle Rush
- [ ] Rebalance economy for 4 characters
- [ ] Testing & balance patches

---

### **Phase 3: Secondary Mode (3-4 weeks)**
- [ ] Capture the Flag
- [ ] CTF balancing
- [ ] Character rebalance

---

### **Phase 4: Polish & Growth (ongoing)**
- [ ] Cosmetic skins
- [ ] Matchmaking improvements (Elo)
- [ ] Leaderboards
- [ ] Tournaments

---

## NOTAS IMPORTANTES

**ArtesanoGames Philosophy:**
- Cada personaje es único: arma + movimiento específico
- No reutilización genérica de assets
- Calidad > Cantidad
- Distribución artesanal = conexión directa con comunidad

**Prioridades MVP:**
- Game feel sobre gráficos (juego funcional primero)
- Simplicidad en mecánicas (Arma + Movimiento)
- Skill-based gameplay (no RNG)
- Presión temporal en rondas (urgencia)

**Decisiones que no se cierran:**
- Si FishNet no escala, puedes migrar a Photon (pero requiere rewrite networking)
- Si Node.js lento, puedes cambiar a Go/Rust (pero requiere rewrite backend)
- Por eso se prioritiza game design sobre tech (game design no se cambia, tech sí)

---

**Versión**: 0.2 - MVP Locked (diseño final)  
**Estado**: Listo para desarrollo  
**Última actualización**: Feb 2026
