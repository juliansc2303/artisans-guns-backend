# Mobile FPS Controls Setup

## ✅ Cambios Implementados

### 1. **UIDocument Simplificado en GameScene**
- **Archivo:** `Assets/UI/Game/GameplayHUD.uxml`
- **Cambios:**
  - ❌ Eliminado: Health bar, ammo display, timer, game phase, kill feed, crosshair, joystick virtual, action buttons
  - ✅ Mantenido: Solo botón de Settings (top-right) y overlay de Settings

### 2. **GameplayHUDController Simplificado**
- **Archivo:** `Assets/Scripts/UI/GameplayHUDController.cs`
- **Cambios:**
  - Removida toda lógica de joystick virtual, health, ammo, timer, kill feed
  - Solo maneja el menú de Settings (abrir/cerrar, exit game, end game test)
  - Pausado del juego con `Time.timeScale = 0f` cuando se abre Settings

### 3. **PlayerController - Movimiento FPS Mobile**
- **Archivo:** `Assets/Scripts/Game/PlayerController.cs`
- **Implementaciones nuevas:**

#### 🕹️ **Movimiento con FloatingJoystick**
```csharp
[SerializeField] private Joystick movementJoystick; // FloatingJoystick del Canvas
```
- Auto-detecta el FloatingJoystick en la escena si no está asignado
- Lee `joystick.Horizontal` y `joystick.Vertical` para movimiento
- Movimiento relativo a la rotación del jugador (transform.TransformDirection)

#### 📱 **Camera Look Táctil (Lado Derecho)**
```csharp
private void ProcessCameraLook()
```
- Detecta touches en el **lado derecho de la pantalla** (right half)
- Lado izquierdo es para el joystick de movimiento
- Calcula delta de movimiento del touch para rotar cámara
- **Pitch (arriba/abajo):** Rotación local de la cámara en eje X
- **Yaw (izquierda/derecha):** Rotación del jugador en eje Y
- Límites de pitch: -80° a +80° (evitar girar completamente)

#### 📷 **Enhanced Touch Support**
```csharp
EnhancedTouchSupport.Enable(); // En Spawned()
EnhancedTouchSupport.Disable(); // En OnDestroy()
```
- Usa Unity's Enhanced Touch API para detección precisa de touches
- Tracking individual de touch ID para evitar conflictos

#### ⚙️ **Settings Ajustables**
```csharp
[SerializeField] private float lookSensitivity = 2f; // Sensibilidad de mirada
[SerializeField] private float minPitch = -80f;      // Límite hacia abajo
[SerializeField] private float maxPitch = 80f;       // Límite hacia arriba
[SerializeField] private float moveSpeed = 5f;       // Velocidad de movimiento
[SerializeField] private float jumpForce = 5f;       // Fuerza de salto
```

## 🎮 Cómo Usar

### **Setup en Unity:**
1. Asegúrate de que el FloatingJoystick esté en el Canvas de GameScene
2. El PlayerController auto-detectará el joystick al spawnear
3. (Opcional) Asigna manualmente el joystick en el Inspector del PlayerPrefab

### **Controles Móviles:**
- **Lado Izquierdo:** Joystick flotante para movimiento (WASD equivalente)
- **Lado Derecho:** Touch y drag para mirar alrededor (mouse look)
- **Botón Settings:** Top-right corner para pausar

### **Testing en Editor:**
- El joystick funciona con mouse en el Editor
- Para probar camera look, usa Touch Simulation en Game View
- O construye APK para probar en dispositivo real

## 🔧 Ajustes Recomendados

### **Para Sensibilidad de Cámara:**
Edita en PlayerPrefab Inspector:
- `lookSensitivity = 2.0f` (default)
- Aumentar para respuesta más rápida
- Disminuir para control más preciso

### **Para Velocidad de Movimiento:**
- `moveSpeed = 5f` (default - walking speed)
- `moveSpeed = 8f` (running speed)

### **Para Campo de Visión Vertical:**
- `minPitch = -80f` (límite hacia abajo)
- `maxPitch = 80f` (límite hacia arriba)
- No usar ±90° para evitar gimbal lock

## 📐 Sistema de Aspect Ratio 16:9

Se mantiene la implementación anterior:
- Letterbox/pillarbox automático en resoluciones no 16:9
- Background camera (depth=-100) para barras negras
- Main camera preserva Skybox del prefab

## 🐛 Fixes Incluidos

1. ✅ **PersistentUI ocultado en GameScene** - No más UI fantasma del lobby
2. ✅ **Skybox preservation** - No se sobrescribe con SolidColor
3. ✅ **Background camera para letterbox** - Barras negras correctas

## 📝 Próximos Pasos (TODO)

- [ ] Agregar botón de salto en Canvas (jump button)
- [ ] Implementar sistema de disparo (shoot button)
- [ ] Agregar indicador visual de touch en lado derecho (crosshair dinámico)
- [ ] Sistema de sensitivity ajustable desde Settings overlay
- [ ] Implementar crouch/sprint con botones adicionales

## 🔍 Debugging

Si el movimiento no funciona:
1. Verifica que FloatingJoystick esté activo en Canvas
2. Revisa Console para mensaje: "✅ FloatingJoystick found automatically"
3. Asigna manualmente en Inspector si no se auto-detecta

Si camera look no responde:
1. Verifica que Enhanced Touch esté habilitado (mensaje en Console)
2. Prueba en dispositivo móvil real (Editor tiene limitaciones)
3. Ajusta `lookSensitivity` si es muy lento/rápido

## 📱 Build Settings para Mobile

**Android:**
```
Minimum API Level: 24 (Android 7.0)
Target API Level: 33 (Android 13)
```

**iOS:**
```
Minimum iOS Version: 12.0
Target Device: iPhone + iPad
```

Asegúrate de que Input System esté configurado en Project Settings → Player → Active Input Handling → Both
