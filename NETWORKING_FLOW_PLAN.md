# Plan de Flujo Completo: Auth → Matchmaking → Gameplay

## Tu Pregunta es Clave 🎯

Tienes razón al querer establecer el flujo completo ANTES de implementar gameplay. Implementar gameplay sin networking = refactorización masiva después.

---

## Flujo Realista (Orden de Implementación)

### ✅ Fase 1: Autenticación (COMPLETADO)
```
LoginScene → Firebase Auth → LobbyScene
```
- [x] AuthManager funcional
- [x] Firebase Anonymous Auth
- [x] Persistencia de sesión
- [x] Scene transitions

### 🔄 Fase 2: Estructura de Networking (PRÓXIMO)
```
LobbyScene → Matchmaking → GameScene → Character Spawn
```

#### 2.1 - Instalar FishNet
```bash
# Via Package Manager
- Abrir Window > Package Manager
- Add package from git URL
- https://github.com/FirstGearGames/FishNet.git
```

#### 2.2 - Configurar Escenas Base
```
Assets/Scenes/
├── LoginScene.unity      ✅ Ya existe
├── LobbyScene.unity      ✅ Ya existe (vacía)
├── GameScene.unity       ⭐ Crear (arena de juego)
└── Bootstrap.unity       ⭐ Crear (NetworkManager persistent)
```

#### 2.3 - Configurar NetworkManager
```csharp
// Bootstrap scene con FishNet NetworkManager
- NetworkManager (DontDestroyOnLoad)
- ServerManager (maneja conexiones)
- ClientManager (maneja cliente)
- TransportManager (Tugboat o FishyFacepunch)
```

#### 2.4 - Implementar Lobby UI Básico
```csharp
LobbyScene:
├── UserInfo Panel (nombre, stats)
├── Character Display (modelo CRIMSON)
├── Find Match Button
└── Status Text (searching, connecting, etc.)
```

### 🎮 Fase 3: Matchmaking Básico (CRÍTICO)

**Opción A: Matchmaking Local (MVP rápido)**
```csharp
// Para testing rápido - sin backend
FindMatchButton.onClick → StartHost() o StartClient("localhost")
- Editor actúa como Host
- Build móvil se conecta como Client
```

**Opción B: Matchmaking con Backend (Producción)**
```javascript
// Node.js + Redis
POST /api/matchmaking/queue
→ Backend asigna servidor
→ Retorna IP:Port
→ Cliente se conecta via FishNet
```

### 🚀 Fase 4: Character Spawning
```csharp
// NetworkBehaviour base para todos los personajes
OnServerStart → Spawn player character
OnClientStart → Setup local player controls
```

### 🎯 Fase 5: CRIMSON Gameplay (SOBRE NETWORK)
Ahora SÍ implementar gameplay, pero ya con:
- NetworkBehaviour
- NetworkTransform
- [ServerRpc] para inputs
- [ObserversRpc] para efectos visuales

---

## Testing con FishNet en Dispositivo 📱

### Setup de Testing Recomendado

#### Opción 1: Editor como Host + Android como Client (FÁCIL)
```
1. Editor (PC):
   - Play Mode
   - Click "Start Host" 
   - Actúa como servidor + cliente local
   
2. Android Device:
   - Build APK
   - Ingresar IP de tu PC en UI
   - Click "Connect to Server"
   - Se conecta al Editor
```

**Ventajas:**
- ✅ Testing inmediato
- ✅ Debug fácil en Editor
- ✅ Logs visibles en consola
- ✅ Un solo dispositivo necesario

**Configuración:**
```csharp
// En Lobby, agregar campo de IP
[SerializeField] private TMP_InputField serverIPInput;

void OnConnectClicked()
{
    string ip = serverIPInput.text; // Ej: "192.168.1.100"
    NetworkManager.ClientManager.StartConnection(ip, 7770);
}
```

#### Opción 2: ParrelSync para Múltiples Clientes (TESTING AVANZADO)
```
ParrelSync permite clonar el proyecto para:
- Editor 1: Host
- Editor 2: Client 1
- Editor 3: Client 2
- Android: Client 3
```

**Instalación:**
```
1. Window > Package Manager
2. Add from Git URL: 
   https://github.com/VeriorPies/ParrelSync.git
3. ParrelSync > Clones Manager > Create New Clone
```

#### Opción 3: Servidor Dedicado en PC (REALISTA)
```
1. Build servidor headless (sin UI):
   - Target: Windows/Linux Server
   - Server Build = true
   
2. Ejecutar en PC:
   - ServerBuild.exe --port 7770
   
3. Clientes se conectan:
   - Editor, Android, iOS → IP del server
```

### Tu Flujo de Testing Típico

```
Día a día (desarrollo rápido):
├── Editor como Host (Play Mode)
└── Android Build como Client
    └── Se conecta a IP de tu PC en WiFi local
    
Testing multijugador:
├── ParrelSync Clone 1: Host
├── ParrelSync Clone 2: Client 1
├── ParrelSync Clone 3: Client 2
└── Android: Client 3

Testing pre-producción:
├── Servidor dedicado en nube (AWS/DigitalOcean)
└── Múltiples dispositivos Android/iOS
```

---

## Estructura de Código Preparada para Networking

### Arquitectura Recomendada

```
Assets/Scripts/
├── Auth/
│   ├── AuthManager.cs              ✅ Ya existe
│   └── LoginManager.cs             ✅ Ya existe
│
├── Networking/
│   ├── NetworkBootstrap.cs         ⭐ NetworkManager setup
│   ├── MatchmakingManager.cs       ⭐ Lobby → Server connection
│   └── GameSessionManager.cs       ⭐ Maneja estado de partida
│
├── Characters/
│   ├── CharacterBase.cs            ⭐ NetworkBehaviour base
│   ├── Crimson/
│   │   ├── CrimsonController.cs    ⭐ Input + movement (networked)
│   │   ├── TalonBurst.cs           ⭐ Weapon (networked)
│   │   └── AerialBurst.cs          ⭐ Ability (networked)
│   └── CharacterSpawner.cs         ⭐ Spawn players en servidor
│
└── GameModes/
    └── TerritorialControl/
        ├── TerritorialControlManager.cs
        ├── CaptureZone.cs
        └── RoundManager.cs
```

### Ejemplo: Character Controller Networked

```csharp
using FishNet.Object;
using UnityEngine;

public class CrimsonController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    
    private CharacterController characterController;
    
    public override void OnStartClient()
    {
        base.OnStartClient();
        
        // Solo el cliente local controla su personaje
        if (!base.IsOwner)
        {
            // Deshabilitar input para otros jugadores
            GetComponent<PlayerInput>().enabled = false;
        }
    }
    
    private void Update()
    {
        // Solo el owner procesa input
        if (!base.IsOwner) return;
        
        HandleMovement();
    }
    
    private void HandleMovement()
    {
        // Input local
        Vector2 input = GetMovementInput();
        
        // Aplicar movimiento localmente (client-side prediction)
        Vector3 move = new Vector3(input.x, 0, input.y) * moveSpeed * Time.deltaTime;
        characterController.Move(move);
        
        // Enviar al servidor para validación
        ServerMove(move);
    }
    
    [ServerRpc]
    private void ServerMove(Vector3 movement)
    {
        // Servidor valida y replica a otros clientes
        // NetworkTransform se encarga de sincronizar posición
    }
}
```

---

## Plan de Implementación (Orden Correcto) 📋

### Semana 1-2: Networking Foundation
- [x] Auth system ✅
- [ ] Instalar FishNet
- [ ] Configurar NetworkManager (Bootstrap scene)
- [ ] Crear GameScene básico
- [ ] Implementar Lobby UI
- [ ] Matchmaking local (Editor Host + Android Client)
- [ ] Character spawning básico
- [ ] Testing: Conectar Android a Editor

**Resultado:** Puedes conectar tu Android al Editor, aparecer en la partida, ver otros jugadores.

### Semana 3-4: CRIMSON Gameplay (Networked desde día 1)
- [ ] CrimsonController (NetworkBehaviour)
- [ ] Character movement (NetworkTransform)
- [ ] Talon Burst weapon (ServerRpc disparos)
- [ ] Aerial Burst ability (sincronizado)
- [ ] Testing: 2 clients moviéndose y disparando

### Semana 5-6: Territorial Control Mode
- [ ] Capture zones (NetworkBehaviour)
- [ ] Zone control logic (Server authority)
- [ ] Round system
- [ ] Win conditions
- [ ] Testing: Partida completa 2v2

### Semana 7-8: Backend Matchmaking (si es necesario)
- [ ] Node.js server
- [ ] Redis queue
- [ ] Matchmaking endpoint
- [ ] FishNet server spawning

---

## Configuración de Red para Testing Local

### Tu PC y Android en misma WiFi:

1. **Encontrar IP de tu PC:**
```powershell
ipconfig
# Buscar "IPv4 Address" de tu adaptador WiFi
# Ejemplo: 192.168.1.100
```

2. **Configurar Firewall de Windows:**
```powershell
# Permitir puerto 7770 (FishNet default)
New-NetFirewallRule -DisplayName "FishNet Server" -Direction Inbound -Protocol TCP -LocalPort 7770 -Action Allow
New-NetFirewallRule -DisplayName "FishNet Server UDP" -Direction Inbound -Protocol UDP -LocalPort 7770 -Action Allow
```

3. **En Unity (Lobby UI):**
```csharp
// Detectar automáticamente si es build o editor
void Start()
{
#if UNITY_EDITOR
    // En Editor: Actuar como Host
    ShowHostButton();
#else
    // En Build: Actuar como Client
    ShowConnectUI();
    serverIPInput.text = "192.168.1.100"; // Default a tu PC
#endif
}
```

---

## Próximos Pasos Inmediatos 🚀

### 1. Instalar FishNet (HOY)
```
Window > Package Manager > + > Add from Git URL
https://github.com/FirstGearGames/FishNet.git?path=/Assets/FishNet
```

### 2. Crear Bootstrap Scene (HOY)
```
- Scene vacía con NetworkManager
- DontDestroyOnLoad
- Carga aditivamente LoginScene
```

### 3. Actualizar Lobby Scene (HOY)
```
- UI: Character display, Find Match button
- Script: MatchmakingManager
- Lógica: StartHost() o StartClient(ip)
```

### 4. Crear GameScene Básico (HOY)
```
- Terreno simple
- Spawn points
- Iluminación
```

### 5. Testing Básico (HOY)
```
- Editor: Play → Start Host
- Android Build: Connect to [TU_IP]
- Verificar: Cliente se conecta correctamente
```

---

## Respuesta a tu Pregunta Original

**"¿Es difícil hacer pruebas con FishNet en dispositivo?"**
- ❌ No, es bastante fácil
- ✅ Editor como Host + Android como Client es el setup estándar
- ✅ WiFi local = testing inmediato sin servidor en nube
- ✅ ParrelSync permite testing con múltiples clientes sin builds

**"¿Qué es más sabio hacer para tener el flujo preparado?"**
- ✅ **Implementar networking PRIMERO** (100% correcto)
- ✅ Orden: Auth → Lobby → Matchmaking → Spawning → Gameplay
- ✅ Todo el gameplay se implementa SOBRE NetworkBehaviour desde día 1
- ✅ Evitas refactorización masiva

---

## ¿Empezamos con FishNet ahora?

Puedo ayudarte a:
1. Instalar FishNet
2. Configurar NetworkManager
3. Crear la estructura de escenas (Bootstrap, Lobby mejorado, GameScene)
4. Implementar matchmaking local simple
5. Setup de testing (Editor Host + Android Client)

Con esto listo, cuando implementes CRIMSON ya será networked desde el inicio y podrás hacer pruebas realistas con tu dispositivo conectándose al Editor.

**¿Procedemos con la instalación de FishNet y el setup básico?** 🎮
