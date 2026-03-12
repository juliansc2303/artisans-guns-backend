# Guía de Configuración de Networking - Unity Setup

## ⚠️ IMPORTANTE: Sigue estos pasos EN ORDEN

Unity necesitará reimportarse después de agregar FishNet. Una vez que Unity termine de importar, sigue esta guía.

---

## Paso 1: Verificar Instalación de FishNet ✅

1. Abrir Unity Editor
2. Esperar a que compile (puede tomar 2-3 minutos)
3. Verificar en Project Window: `Packages/Fish-Net` debe aparecer
4. Window > Package Manager → Buscar "Fish-Net" en la lista

Si aparece ✅, continuar. Si no ❌, reportar error.

---

## Paso 2: Crear Bootstrap Scene 🌐

### 2.1 - Crear la Scene
1. File > New Scene
2. Nombrar: `Bootstrap`
3. Eliminar todo (Main Camera, Directional Light)

### 2.2 - Agregar NetworkManager
1. GameObject > Create Empty → Nombrar `NetworkManager`
2. Add Component > FishNet > `Network Manager`
3. En Network Manager component:
   - **Transport**: Seleccionar `Tugboat` (default)
   - **Client Manager**: Verificar que esté asignado automáticamente
   - **Server Manager**: Verificar que esté asignado automáticamente

### 2.3 - Agregar NetworkBootstrap Script
1. Seleccionar GameObject `NetworkManager`
2. Add Component > Buscar `Network Bootstrap`
3. En el script:
   - **Initial Scene Name**: `LoginScene`

### 2.4 - Guardar Scene
1. File > Save Scene As...
2. Guardar en: `Assets/Scenes/Bootstrap.unity`

---

## Paso 3: Configurar Build Settings 🔧

1. File > Build Settings
2. Arrastrar escenas en ESTE ORDEN:
   ```
   [0] Bootstrap.unity       ← Primera escena (la que se carga al iniciar)
   [1] LoginScene.unity
   [2] LobbyScene.unity
   [3] GameScene.unity       ← (la crearemos después)
   ```
3. Click "Close"

⚠️ **CRÍTICO**: Bootstrap DEBE ser index 0 para que se cargue primero.

---

## Paso 4: Crear GameScene 🎮

### 4.1 - Crear Scene Base
1. File > New Scene
2. Nombrar: `GameScene`
3. Crear un terreno/piso simple:
   - GameObject > 3D Object > Plane (escalar a 10, 1, 10)
   - Posición: (0, 0, 0)
   - Nombrar: `Ground`

### 4.2 - Agregar Spawn Points
1. GameObject > Create Empty → Nombrar `SpawnPoints`
2. Crear 4 empty children:
   ```
   SpawnPoints/
   ├── SpawnPoint_1 (Position: -5, 0, -5)
   ├── SpawnPoint_2 (Position: 5, 0, -5)
   ├── SpawnPoint_3 (Position: -5, 0, 5)
   └── SpawnPoint_4 (Position: 5, 0, 5)
   ```

### 4.3 - Agregar CharacterSpawner
1. GameObject > Create Empty → Nombrar `CharacterSpawner`
2. Add Component > Buscar `Character Spawner`
3. Add Component > FishNet > `Network Object`
4. En Character Spawner:
   - **Spawn Points**: Arrastrar los 4 spawn points
   - **Auto Spawn On Connect**: ✅ Check
   - **Crimson Prefab**: Dejar vacío por ahora (lo crearemos después)

### 4.4 - Configurar Lighting Básico
1. Agregar Directional Light (si no existe)
2. Window > Rendering > Lighting
3. Environment:
   - Skybox Material: Default
   - Sun Source: Directional Light

### 4.5 - Guardar Scene
1. File > Save Scene As...
2. Guardar en: `Assets/Scenes/GameScene.unity`

---

## Paso 5: Actualizar LobbyScene 🎨

### 5.1 - Abrir LobbyScene
1. Abrir `Assets/Scenes/LobbyScene.unity`

### 5.2 - Agregar MatchmakingManager
1. GameObject > Create Empty → Nombrar `MatchmakingManager`
2. Add Component > Buscar `Matchmaking Manager`
3. En el component:
   - **Game Scene Name**: `GameScene`
   - **Server Port**: `7770`

### 5.3 - Crear UI de Lobby

#### Canvas Base
1. GameObject > UI > Canvas (si no existe)
2. Canvas Settings:
   - Render Mode: Screen Space - Overlay
   - UI Scale Mode: Scale With Screen Size
   - Reference Resolution: 1920x1080

#### Panel: Host/Client Selection (Para Editor)
```
Canvas/
└── HostClientPanel
    ├── WelcomeText (TextMeshPro)
    ├── LocalIPText (TextMeshPro) - "Your IP: ..."
    ├── StartHostButton (Button)
    │   └── Text: "Start as Host"
    └── StartClientButton (Button)
        └── Text: "Connect to Server"
```

#### Panel: Connection (Para Build móvil)
```
Canvas/
└── ConnectPanel
    ├── TitleText (TextMeshPro) - "Enter Server IP"
    ├── ServerIPInputField (TMP_InputField)
    │   └── Placeholder: "192.168.1.100"
    ├── ConnectButton (Button)
    │   └── Text: "Connect"
    └── BackButton (Button)
        └── Text: "Back"
```

#### Elementos Compartidos
```
Canvas/
├── StatusText (TextMeshPro) - "Status: ..."
├── LoadingPanel (GameObject)
│   └── LoadingText (TextMeshPro) - "Connecting..."
└── SignOutButton (Button)
    └── Text: "Sign Out"
```

### 5.4 - Agregar LobbyUIManager
1. Seleccionar Canvas
2. Add Component > Buscar `Lobby UI Manager`
3. Arrastrar todas las referencias de UI creadas arriba

### 5.5 - Guardar LobbyScene
1. File > Save Scene

---

## Paso 6: Crear CRIMSON Prefab 🦅

### 6.1 - Crear GameObject Base
1. En GameScene, crear: GameObject > Create Empty → `CRIMSON`
2. Position: (0, 0, 0)

### 6.2 - Agregar Componentes Base
En orden:
1. **Character Controller**:
   - Height: 2
   - Radius: 0.5
   - Center: (0, 1, 0)

2. **Network Object** (FishNet):
   - Is Spawnable: ✅
   - Is Global: ❌
   - Default Despawn Type: Destroy

3. **Network Transform** (FishNet):
   - **IMPORTANTE**: Este componente sincroniza posición/rotación automáticamente
   - Synchronize Position: ✅
   - Synchronize Rotation: ✅
   - Interpolation: 0.15 (default)
   - ⚠️ En FishNet 4.x, NetworkTransform reemplaza el uso de [SyncVar]

4. **Crimson Controller** (nuestro script):
   - Move Speed: 5
   - Rotation Speed: 10

### 6.3 - Agregar Visual Placeholder
1. Agregar child: GameObject > 3D Object > Capsule
2. Nombrar: `Visual`
3. Position: (0, 1, 0)
4. Scale: (1, 1, 1)
5. Material: Crear material rojo y asignar

### 6.4 - Crear Prefab
1. Arrastrar `CRIMSON` GameObject desde Hierarchy a `Assets/Prefabs/Characters/`
2. Crear carpeta si no existe: `Assets/Prefabs/Characters/`
3. Eliminar CRIMSON de la escena (ya está como prefab)

### 6.5 - Asignar Prefab a CharacterSpawner
1. En GameScene, seleccionar `CharacterSpawner`
2. En Character Spawner component:
   - **Crimson Prefab**: Arrastrar prefab CRIMSON desde carpeta Prefabs

### 6.6 - Guardar GameScene
1. File > Save Scene

---

## Paso 7: Testing en Editor 🧪

### 7.1 - Configurar para Testing
1. File > Build Settings
2. Verificar que Bootstrap sea index [0]

### 7.2 - Primera Prueba: Host Local
1. Play Mode
2. En LobbyScene, debería ver panel de Host/Client
3. Click "Start as Host"
4. Verificar en Console:
   ```
   🌐 NetworkBootstrap initialized
   🎮 Starting as Host...
   ✅ Server started successfully
   ✅ Connected to server
   📦 Loading GameScene on server...
   ✅ CharacterSpawner initialized
   🎮 Player connected
   ✅ Spawned character
   ```
5. GameScene debería cargarse
6. Tu personaje CRIMSON debería aparecer
7. Usar WASD para mover

### 7.3 - Testing con ParrelSync (Opcional)
Si quieres probar con múltiples clientes en Editor:

1. Window > Package Manager
2. + > Add from Git URL: `https://github.com/VeriorPies/ParrelSync.git`
3. ParrelSync > Clones Manager > Create New Clone
4. Abrir el clone
5. Editor principal: Start as Host
6. Clone: Start as Client (usar IP: localhost o 127.0.0.1)

---

## Paso 8: Testing en Android 📱

### 8.1 - Obtener IP de tu PC
1. En Windows PowerShell:
   ```powershell
   ipconfig
   ```
2. Buscar "IPv4 Address" de tu adaptador WiFi
3. Ejemplo: `192.168.1.100`
4. Anotar esta IP

### 8.2 - Configurar Firewall
```powershell
New-NetFirewallRule -DisplayName "FishNet Server" -Direction Inbound -Protocol TCP -LocalPort 7770 -Action Allow
New-NetFirewallRule -DisplayName "FishNet Server UDP" -Direction Inbound -Protocol UDP -LocalPort 7770 -Action Allow
```

### 8.3 - Build para Android
1. File > Build Settings
2. Platform: Android
3. Switch Platform (si no está activo)
4. Player Settings:
   - Company Name: ArtesanoGames
   - Product Name: Artisans Guns
   - Package Name: com.artesanogames.birdhead
5. Build APK
6. Instalar en dispositivo Android

### 8.4 - Testing Editor + Android
1. **PC (Editor)**:
   - Play Mode
   - En LobbyScene → "Start as Host"
   - Ver tu IP en pantalla

2. **Android Device**:
   - Abrir app
   - Login (Anonymous Auth)
   - En LobbyScene:
     - Debería mostrar panel de "Connect"
     - Ingresar IP de tu PC (ej: 192.168.1.100)
     - Click "Connect"

3. **Resultado esperado**:
   - Android se conecta a Editor
   - GameScene carga en ambos
   - Aparecen 2 personajes CRIMSON
   - Puedes moverte en Editor (WASD)
   - Android ve tu movimiento en tiempo real
   - Android puede moverse (touch/joystick cuando lo implementemos)

---

## Paso 9: Verificar que Todo Funciona ✅

### Checklist de Testing:

- [ ] FishNet instalado correctamente
- [ ] Bootstrap scene carga primero
- [ ] LoginScene → LobbyScene funciona
- [ ] En Editor: Puedo hacer "Start as Host"
- [ ] GameScene se carga automáticamente
- [ ] Mi personaje CRIMSON spawns
- [ ] Puedo mover con WASD
- [ ] En Android: Puedo conectarme a Editor
- [ ] Android ve mi personaje moverse
- [ ] Console no muestra errores críticos

---

## Troubleshooting 🔧

### Error: "NetworkManager not found"
- Verificar que Bootstrap scene esté como index [0] en Build Settings
- Verificar que NetworkBootstrap script esté en el GameObject NetworkManager

### Error: "Character prefab not spawnable"
- Verificar que CRIMSON prefab tenga NetworkObject component
- Verificar que "Is Spawnable" esté ✅
- Verificar que "Is Player" esté ✅

### Android no se conecta
- Verificar que PC y Android estén en misma WiFi
- Verificar firewall de Windows (Paso 8.2)
- Probar con IP correcta (ipconfig en PC)
- Verificar que puerto 7770 no esté bloqueado

### Personaje no se mueve
- Verificar que CrimsonController esté en el prefab
- Verificar que CharacterController esté configurado
- Verificar logs: "IsOwner: True" debe aparecer para jugador local

### GameScene no carga
- Verificar que GameScene esté en Build Settings
- Verificar nombre en MatchmakingManager: "GameScene" (case-sensitive)

---

## Próximos Pasos 🚀

Una vez que todo esto funcione:

1. **Input System**: Implementar controles touch para móvil
2. **Camera Follow**: Script para que cámara siga al jugador
3. **Talon Burst Weapon**: Sistema de disparo networked
4. **Aerial Burst**: Habilidad de dash networked
5. **UI de Gameplay**: HUD, health bars, etc.
6. **Territorial Control**: Zones y lógica de juego

Pero primero, **asegúrate de que el flujo básico de conexión funcione**.

---

## Referencias Rápidas

**Documentación FishNet**: https://fish-networking.gitbook.io/docs/
**Discord FishNet**: https://discord.gg/Ta9HgDh4Hj

**Conceptos clave**:
- `NetworkBehaviour`: Base class para scripts networked
- `[ServerRpc]`: Cliente llama, servidor ejecuta
- `[ObserversRpc]`: Servidor llama, todos los clientes ejecutan
- `[SyncVar]`: Variable sincronizada automáticamente
- `NetworkObject`: Componente requerido para spawning
- `NetworkTransform`: Sincroniza posición/rotación automáticamente
