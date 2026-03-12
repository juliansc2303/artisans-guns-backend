# 🔫 WEAPON SYSTEM SETUP GUIDE

Sistema de armas conectado con el backend loadout. Las armas se instancian automáticamente basadas en la selección del jugador en el Lobby.

---

## 📁 ARCHIVOS CREADOS

### Scripts
- ✅ `Assets/Scripts/Weapons/WeaponConfig.cs` - ScriptableObject para configurar armas
- ✅ `Assets/Scripts/Weapons/FireWeapon.cs` - Lógica de disparo (en prefab del arma)
- ✅ `Assets/Scripts/Weapons/WeaponRecoil.cs` - Sistema de recoil (en prefab del arma)
- ✅ `Assets/Scripts/Game/PlayerSetup.cs` - Maneja instanciación de armas (en PlayerPrefab)

### ScriptableObjects (Resources)
- ✅ `Assets/Resources/Weapons/TalonAR.asset` - Config para talon_ar
- ✅ `Assets/Resources/Weapons/Bolt.asset` - Config para bolt

---

## 🛠️ SETUP EN UNITY (PASO A PASO)

### 1. **Configurar PlayerPrefab**

En tu **PlayerPrefab_TeamA** y **PlayerPrefab_TeamB**:

1. **Agregar PlayerSetup Component**:
   - Selecciona PlayerPrefab en el Project
   - Add Component → Player Setup
   
2. **Crear WeaponHolder** (punto de spawn del arma):
   - Click derecho en PlayerPrefab → Create Empty
   - Nombre: `WeaponHolder`
   - Posición sugerida: `(0.2, 0.5, 0.3)` (ajustar según tu cámara)
   - Este será el parent del arma
   
3. **Crear RightHandGrip** (IK target mano derecha):
   - Click derecho en PlayerPrefab → Create Empty
   - Nombre: `RightHandGrip`
   - Posición sugerida: `(0.15, 0.4, 0.25)`
   
4. **Crear LeftHandGrip** (IK target mano izquierda):
   - Click derecho en PlayerPrefab → Create Empty
   - Nombre: `LeftHandGrip`
   - Posición sugerida: `(0.05, 0.35, 0.4)`

5. **Asignar Referencias en PlayerSetup**:
   - WeaponHolder → arrastra transform `WeaponHolder`
   - RightHandGrip → arrastra transform `RightHandGrip`
   - LeftHandGrip → arrastra transform `LeftHandGrip`

---

### 2. **Crear Prefabs de Armas**

Para **TALON-AR**:

1. **Crear GameObject del arma**:
   - En la escena, crea un Empty: `TalonAR_Prefab`
   - Importa o crea el modelo 3D del TALON-AR como child
   
2. **Crear FirePoint**:
   - Click derecho en TalonAR_Prefab → Create Empty
   - Nombre: `FirePoint`
   - Posiciona en la punta del cañón (donde salen las balas)
   
3. **Agregar Components**:
   - Add Component → Fire Weapon
   - Add Component → Weapon Recoil
   - Add Component → Audio Source
   
4. **Configurar FireWeapon**:
   - Fire Point → arrastra el transform `FirePoint`
   
5. **Convertir a Prefab**:
   - Arrastra `TalonAR_Prefab` a `Assets/Prefabs/Weapons/` (crea carpeta si no existe)
   - Elimina de la escena

**Repetir para BOLT** (nombre: `Bolt_Prefab`)

---

### 3. **Configurar ScriptableObjects**

En **Project → Assets/Resources/Weapons/**:

#### TalonAR.asset:
1. Selecciona el archivo
2. En Inspector:
   - **Weapon Id**: `talon_ar` ✅ (ya configurado)
   - **Weapon Name**: `TALON-AR` ✅
   - **Weapon Prefab**: Arrastra `TalonAR_Prefab` aquí
   - **Fire Rate**: 600 (10 disparos/seg)
   - **Fire Sound**: Arrastra AudioClip del disparo
   - **Vertical Recoil**: 1.2
   - **Horizontal Recoil**: 0.4
   - **Recoil Recovery Speed**: 3
   - **Position Offset**: Ajusta posición del arma en mano (ej: `0.15, -0.1, 0.3`)
   - **Rotation Offset**: Ajusta rotación (ej: `0, 0, 0`)

#### Bolt.asset:
- Igual que TalonAR, pero con:
  - **Weapon Id**: `bolt` ✅
  - **Weapon Name**: `BOLT` ✅
  - **Weapon Prefab**: `Bolt_Prefab`
  - **Fire Rate**: 300 (5 disparos/seg, más lento)
  - **Vertical Recoil**: 2.5 (más recoil)
  - **Horizontal Recoil**: 0.8

---

### 4. **Agregar FireButton al Canvas (GameScene)**

En **GameScene → Canvas**:

1. **Crear Button**:
   - Click derecho en Canvas → UI → Button - TextMeshPro
   - **Nombre: `FireButton`** (IMPORTANTE: debe llamarse exactamente así)
   
2. **Configurar Button**:
   - Posición: Esquina inferior derecha
   - Tamaño sugerido: `100x100` pixels
   - Cambiar texto a "🔥" o ícono de disparo
   
3. **NO necesitas asignar evento onClick manualmente** - FireWeapon lo encuentra automáticamente por nombre

---

## 🔄 FLUJO DE FUNCIONAMIENTO

```
1. Usuario selecciona arma en Lobby (backend)
   ↓
2. LoadoutManager guarda weaponId ("talon_ar" o "bolt")
   ↓
3. Player spawns en GameScene
   ↓
4. PlayerSetup.Spawned() se ejecuta
   ↓
5. LoadWeaponsFromLoadout() lee weaponId del backend
   ↓
6. LoadWeaponConfigById() carga ScriptableObject (TalonAR.asset)
   ↓
7. SpawnPrimaryWeapon() instancia el prefab
   ↓
8. FireWeapon.Initialize() configura fire rate, recoil, etc.
   ↓
9. FindFireButton() conecta con Button del Canvas
   ↓
10. ✅ Jugador tiene arma correcta y puede disparar
```

---

## 🧪 TESTING

### Verificar Backend Connection:
1. En Unity, ejecuta el juego
2. Login con usuario que tenga `talon_ar` como primaryWeapon
3. Entra a LobbyScene
4. Consola debe mostrar:
   ```
   ✅ [LoadoutManager] Loadout initialized for [username]
      Primary: talon_ar - default
   ```
5. Crea room y entra a GameScene
6. Consola debe mostrar:
   ```
   🔫 [PlayerSetup] Starting weapon setup for local player
   ✅ [PlayerSetup] Loaded primary weapon: talon_ar -> TALON-AR
   🔫 [PlayerSetup] Weapon spawned and initialized: TALON-AR
   🔫 [FireWeapon] Initialized: TALON-AR (Fire Rate: 600 RPM, Interval: 0.100s)
   ✅ [FireWeapon] FireButton found and connected
   ```

### Test Shooting:
1. En GameScene, presiona `FireButton`
2. Consola debe mostrar:
   ```
   🔥 [FireWeapon] Fired TALON-AR
   💥 [WeaponRecoil] Applied recoil: H=0.23, V=1.20
   ```
3. Deberías ver:
   - Audio de disparo (si asignaste clip)
   - Recoil en cámara
   - Raycast debug lines (rojo si hit, amarillo si miss)

---

## 🎯 PRÓXIMOS PASOS (NO IMPLEMENTADOS AUN)

- [ ] Sistema de daño (Health component en enemigos)
- [ ] Cambio de arma (primary ↔ secondary)
- [ ] Munición y recarga
- [ ] Efectos visuales (muzzle flash, bullet tracer, impacto)
- [ ] Networking de disparos (RPC para sincronizar)
- [ ] Animaciones de armas
- [ ] Sistema de IK para manos en grips
- [ ] UI de ammo counter

---

## ⚙️ CONFIGURACIÓN POR DEFECTO

Si LoadoutManager no está inicializado, el sistema usa:
- **Primary Weapon**: TalonAR (talon_ar)
- **Secondary Weapon**: Bolt (bolt)

---

## 🔧 TROUBLESHOOTING

### "WeaponConfig not found for ID: talon_ar"
- Verifica que exista `Assets/Resources/Weapons/TalonAR.asset`
- Verifica que el `weaponId` en el ScriptableObject sea exactamente `talon_ar`

### "WeaponHolder transform not assigned!"
- En PlayerPrefab, asigna referencias en PlayerSetup component

### "FireButton not found in scene"
- En GameScene Canvas, crea Button con nombre exacto `FireButton`

### "FireWeapon component not found on weapon prefab!"
- En el prefab del arma, agrega FireWeapon y WeaponRecoil scripts

### Arma no se ve o está en posición incorrecta
- Ajusta `positionOffset` y `rotationOffset` en el ScriptableObject
- Verifica que WeaponHolder esté en posición correcta en PlayerPrefab

---

## 📝 NOTAS IMPORTANTES

1. **IDs deben coincidir con backend**:
   - Backend usa: `talon_ar`, `bolt`
   - ScriptableObject weaponId debe ser exacto (case-sensitive)

2. **Resources folder es obligatorio**:
   - PlayerSetup usa `Resources.Load<WeaponConfig>("Weapons/TalonAR")`
   - No muevas los .asset fuera de Resources/Weapons/

3. **FireButton detectado por nombre**:
   - NO cambies el nombre del Button
   - Si necesitas otro nombre, modifica `FindFireButton()` en FireWeapon.cs

4. **Solo local player spawns weapon**:
   - PlayerSetup verifica `Object.HasInputAuthority`
   - Jugadores remotos no ejecutan weapon setup (evita duplicados)

---

✅ **Sistema base completado** - Ahora conecta loadout con gameplay
🔧 **Siguiente**: Configurar en Unity Editor siguiendo pasos arriba
