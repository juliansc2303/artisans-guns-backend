# 🎬 ANIMATOR CONTROLLER SETUP GUIDE

Arquitectura de animaciones separadas: **Hands (Player)** y **Weapon (Prefab)**

---

## 📐 ARQUITECTURA

### **Flujo de Sincronización:**

```
FireWeapon.StartReload()
├─ handsAnimator.SetTrigger("Reload")          → Player hands
└─ weaponAnimator.SetTrigger("WeaponReload")   → Weapon parts

Ambas animaciones se reproducen simultáneamente (2.5s)
```

---

## 🎮 PLAYER ANIMATOR SETUP

### **Ubicación:**
- Component: **Animator** en `mixamorig:Spine2`
- Controller: Creado manualmente (ej: `TalonARHands.controller`)

### **Estructura del Controller:**

```
TalonARHands.controller (Player Hands)
├─ Parameters:
│  └─ Reload (Trigger)
│
└─ Layers:
   └─ Base Layer
      ├─ Entry → Idle_AR_Hands
      ├─ Any State → Reload_AR_Hands (cuando Reload trigger)
      └─ Reload_AR_Hands → Idle_AR_Hands (automático al terminar)
```

### **States:**

#### **Idle_AR_Hands**
- AnimationClip: `Idle_AR_Hands.anim`
- Loop: ✅ True
- Contenido: Brazos sosteniendo arma (idle breathing)

#### **Reload_AR_Hands**
- AnimationClip: `Reload_AR_Hands.anim`
- Loop: ❌ False
- Duration: 2.5 segundos
- Contenido:
  ```
  0.0s: Brazos en idle
  0.5s: Mano derecha baja a magazine
  0.8s: Mano saca magazine hacia abajo
  1.0s: Mano va a cintura
  1.2s: Mano regresa con magazine nuevo
  1.5s: Mano inserta magazine
  2.0s: Mano izquierda jala bolt
  2.5s: Regresa a idle
  ```

### **Transitions:**

```
Any State → Reload_AR_Hands:
├─ Conditions: Trigger "Reload"
├─ Has Exit Time: ❌ No (interrupción inmediata)
├─ Transition Duration: 0.1s
└─ Interruption Source: None

Reload_AR_Hands → Idle_AR_Hands:
├─ Conditions: (none, automático)
├─ Has Exit Time: ✅ Yes
├─ Exit Time: 1.0 (al finalizar clip)
└─ Transition Duration: 0.2s (blend suave)
```

---

## 🔫 WEAPON ANIMATOR SETUP

### **Ubicación:**
- Component: **Animator** en el **weapon prefab** (TalonAR_Prefab, Bolt_Prefab)
- Controller: Uno por arma o compartido por clase

### **Estructura del Controller:**

```
TalonAR_Weapon.controller (Weapon Parts)
├─ Parameters:
│  └─ WeaponReload (Trigger)
│
└─ Layers:
   └─ Base Layer
      ├─ Entry → IdleTalon_AR
      ├─ Any State → ReloadTalon_AR (cuando WeaponReload trigger)
      └─ ReloadTalon_AR → IdleTalon_AR (automático)
```

### **States:**

#### **IdleTalon_AR**
- AnimationClip: `IdleTalon_AR.anim`
- Loop: ✅ True
- Contenido: Magazine/bolt en posición default (sin animación o subtle sway)

#### **ReloadTalon_AR**
- AnimationClip: `ReloadTalon_AR_Magazine.anim`
- Loop: ❌ False
- Duration: 2.5 segundos
- Contenido:
  ```
  0.0s: Magazine en arma
  0.5s: Magazine_Transform baja (localPosition.y -= 0.5)
  0.6s: Magazine_Transform SetActive(false)
  1.2s: Magazine_Transform SetActive(true) (nuevo mag)
  1.3s: Magazine_Transform sube (localPosition.y vuelve a 0)
  1.5s: Magazine_Transform locked
  2.0s: Bolt_Transform cycle hacia atrás
  2.2s: Bolt_Transform vuelve adelante
  2.5s: Idle
  ```

### **Transitions:**

```
Any State → ReloadTalon_AR:
├─ Conditions: Trigger "WeaponReload"
├─ Has Exit Time: ❌ No
├─ Transition Duration: 0s (instantáneo)
└─ Interruption Source: None

ReloadTalon_AR → IdleTalon_AR:
├─ Conditions: (none)
├─ Has Exit Time: ✅ Yes
├─ Exit Time: 1.0
└─ Transition Duration: 0.1s
```

---

## 🛠️ SETUP EN UNITY (PASO A PASO)

### **1. Crear Player Animator Controller**

1. **Crear Controller:**
   - `Assets/Animations/Controllers/` → Create → Animator Controller
   - Nombre: `AR_Hands.controller` (compartido por TalonAR y Bolt)

2. **Agregar Parameter:**
   - Window → Animation → Animator
   - Parameters tab → `+` → Trigger
   - Nombre: **`Reload`** (exactamente así)

3. **Crear States:**
   - Botón derecho en grid → Create State → From Clip
   - Selecciona `Idle_AR_Hands.anim` (o crea uno vacío temporalmente)
   - Set as Layer Default State
   - Repetir para `Reload_AR_Hands`

4. **Configurar Transitions:**
   - Click derecho en "Any State" → Make Transition → `Reload_AR_Hands`
   - Selecciona transition → Inspector:
     - Conditions: `+` → Reload
     - Has Exit Time: ❌ Unchecked
     - Transition Duration: 0.1
   - Click en `Reload_AR_Hands` → Make Transition → `Idle_AR_Hands`
     - Has Exit Time: ✅ Checked
     - Exit Time: 1.0

5. **Asignar al Player:**
   - PlayerPrefab → `mixamorig:Spine2` → Inspector
   - Add Component → Animator (si no tiene)
   - Controller: Arrastra `AR_Hands.controller`

---

### **2. Crear Weapon Animator Controller**

1. **Crear Controller:**
   - `Assets/Animations/Controllers/` → Create → Animator Controller
   - Nombre: `TalonAR_Weapon.controller`

2. **Agregar Parameter:**
   - Animator window → Parameters → `+` → Trigger
   - Nombre: **`WeaponReload`** (exactamente así)

3. **Crear States:**
   - Idle: `IdleTalon_AR.anim` (o Empty)
   - Reload: `ReloadTalon_AR_Magazine.anim`

4. **Configurar Transitions:**
   - Any State → ReloadTalon_AR
     - Condition: WeaponReload
     - Has Exit Time: ❌ No
     - Duration: 0
   - ReloadTalon_AR → IdleTalon_AR
     - Has Exit Time: ✅ Yes
     - Exit Time: 1.0

5. **Asignar al Weapon Prefab:**
   - TalonAR_Prefab → Inspector
   - Add Component → Animator
   - Controller: Arrastra `TalonAR_Weapon.controller`

---

### **3. Configurar PlayerSetup**

En PlayerPrefab → PlayerSetup component:

```
Weapon Holder: WeaponHolder (transform)
Right Hand Grip: RightHandGrip (transform)
Left Hand Grip: LeftHandGrip (transform)
Rig Transform: mixamorig:Spine2 (transform con Rig Builder)
Hands Animator: mixamorig:Spine2 (animator component) ← NUEVO
```

---

### **4. Configurar WeaponConfig ScriptableObject**

En `Assets/Resources/Weapons/TalonAR.asset`:

```
Weapon Class: AssaultRifle
Hands Animator Controller: AR_Hands.controller ← Compartido con Bolt
Reload Hands Clip: (vacío, usa default del controller)
Reload Time: 2.5
```

En `Assets/Resources/Weapons/Bolt.asset`:

```
Weapon Class: AssaultRifle
Hands Animator Controller: AR_Hands.controller ← MISMO que TalonAR
Reload Hands Clip: Reload_Bolt_Custom.anim ← OVERRIDE (opcional)
Reload Time: 2.8 (Bolt más lento)
```

---

## 🔄 REUTILIZACIÓN POR CLASE

### **Armas de misma clase comparten Hands Controller:**

```
AR_Hands.controller:
├─ Usado por: TalonAR, Bolt, M4, AK47, etc.
└─ Reload_AR_Hands.anim (animación genérica de brazos)

Pistol_Hands.controller:
├─ Usado por: Ghost, Sheriff, USP, etc.
└─ Reload_Pistol_Hands.anim (grip vertical, magazine desde grip)
```

### **Cada arma puede override el clip de recarga:**

```
TalonAR.asset:
└─ Reload Hands Clip: (vacío) → usa Reload_AR_Hands del controller

Bolt.asset:
└─ Reload Hands Clip: Reload_Bolt_Custom.anim → override específico
```

**Cómo funciona**: PlayerSetup crea AnimatorOverrideController cuando detecta `reloadHandsClip != null`.

---

## 🧪 TESTING

### **Test 1: Verificar Triggers**

1. Play Mode
2. Inspector → PlayerPrefab → mixamorig:Spine2 → Animator
3. Manualmente clickea "Reload" trigger
4. Debe reproducirse animación de brazos

5. Inspector → TalonAR_Prefab → Animator
6. Manualmente clickea "WeaponReload" trigger
7. Debe reproducirse animación de magazine

### **Test 2: Sincronización Automática**

1. Play Mode
2. Console debe mostrar:
   ```
   🔫 [PlayerSetup] Weapon spawned and initialized: TALON-AR
   🎬 [PlayerSetup] Applied hands animator: AR_Hands
   🖐️ [PlayerSetup] Right/Left hand grip positions set
   ```

3. Llama `FireWeapon.StartReload()` (agregar botón temporal)
4. Console debe mostrar:
   ```
   🔄 [FireWeapon] Player hands reload triggered
   🔄 [FireWeapon] Weapon reload triggered
   ⏱️ [FireWeapon] Reload started, duration: 2.5s
   ```

5. Ambas animaciones deben reproducirse simultáneamente
6. Después de 2.5s:
   ```
   ✅ [FireWeapon] Reload complete
   ```

---

## 📊 ESTRUCTURA DE ARCHIVOS

```
Assets/
├─ Animations/
│  ├─ Controllers/
│  │  ├─ AR_Hands.controller          ← Player (TalonAR, Bolt)
│  │  ├─ Pistol_Hands.controller      ← Player (Ghost, Sheriff)
│  │  ├─ TalonAR_Weapon.controller    ← Weapon specific
│  │  └─ Bolt_Weapon.controller       ← Weapon specific
│  │
│  ├─ Hands/
│  │  ├─ Idle_AR_Hands.anim
│  │  ├─ Reload_AR_Hands.anim         ← Genérico para ARs
│  │  ├─ Reload_Bolt_Custom.anim      ← Override específico
│  │  ├─ Idle_Pistol_Hands.anim
│  │  └─ Reload_Pistol_Hands.anim
│  │
│  └─ Weapons/
│     ├─ IdleTalon_AR.anim
│     ├─ ReloadTalon_AR_Magazine.anim
│     ├─ IdleBolt.anim
│     └─ ReloadBolt_Magazine.anim
│
├─ Resources/Weapons/
│  ├─ TalonAR.asset
│  └─ Bolt.asset
│
└─ Prefabs/Weapons/
   ├─ TalonAR_Prefab (tiene Animator con TalonAR_Weapon.controller)
   └─ Bolt_Prefab (tiene Animator con Bolt_Weapon.controller)
```

---

## 🎯 VENTAJAS DE ESTA ARQUITECTURA

| Feature | Beneficio |
|---------|-----------|
| **Controllers por clase** | TalonAR + Bolt comparten AR_Hands (menos trabajo) |
| **Override optional** | Bolt puede tener reload custom sin nuevo controller |
| **Weapon autonomy** | Cada prefab tiene su animator, fácil testear |
| **Sincronización simple** | Un llamado a StartReload() activa ambos |
| **Escalable** | Nuevas armas: asignar controller existente o crear nuevo |
| **Debug friendly** | Logs claros de qué trigger activó qué animación |

---

## ⚠️ TROUBLESHOOTING

### "Hands animation no reproduce"
- ✅ Verifica `handsAnimator` assigned en PlayerSetup
- ✅ Verifica trigger se llama **"Reload"** (case-sensitive)
- ✅ Verifica transition de Any State existe

### "Weapon animation no reproduce"
- ✅ Verifica Animator component en weapon prefab
- ✅ Verifica trigger se llama **"WeaponReload"** (case-sensitive)
- ✅ Verifica AnimationClip asignado al state

### "Animaciones desincronizadas"
- ✅ Ambas deben tener misma duración (weaponConfig.reloadTime)
- ✅ Verifica Exit Time = 1.0 en ambas
- ✅ Ajusta timing de keyframes en clips

### "Override no funciona"
- ✅ Clip base debe llamarse **"Reload_Hands"** en controller
- ✅ WeaponConfig.reloadHandsClip debe estar asignado
- ✅ Check logs: debe decir "Override reload hands clip: ..."

---

## 🚀 PRÓXIMOS PASOS

1. ✅ Crear AR_Hands.controller con Reload trigger
2. ✅ Crear TalonAR_Weapon.controller con WeaponReload trigger
3. ✅ Asignar controllers a PlayerSetup y weapon prefabs
4. ✅ Configurar WeaponConfig.asset con referencias
5. ⏭️ Animar weapon parts (magazine, bolt) primero
6. ⏭️ Animar hands siguiendo weapon parts después
7. ⏭️ Test sincronización
8. ⏭️ Agregar UI button para reload

---

✅ **Arquitectura implementada** - Player y Weapon animators separados
🎬 **Trigger-based sync** - Flexible y escalable
🔄 **Clase-based reuse** - Menos animaciones duplicadas
