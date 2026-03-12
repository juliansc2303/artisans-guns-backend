# ✅ TALON-AR IMPLEMENTATION CHECKLIST

Quick reference para completar el setup de TalonAR (solo primary weapon por ahora).

---

## 📦 ASSETS A CREAR

### **1. ScriptableObject**
- [ ] `Assets/Resources/Weapons/TalonAR.asset`
  - weaponId: `"talon_ar"`
  - weaponName: `"TALON-AR"`
  - weaponClass: `AssaultRifle`
  - weaponPrefab: Asignar después de crear prefab
  - fireRate: `600`
  - reloadTime: `2.5`

### **2. Weapon Prefab**
- [ ] `Assets/Prefabs/Weapons/TalonAR_Prefab`
  - Componentes:
    - [ ] Animator (con controller)
    - [ ] FireWeapon script
    - [ ] WeaponRecoil script
    - [ ] AudioSource
  - Hijos:
    - [ ] TalonAR_Model (mesh)
    - [ ] FirePoint (empty en cañón)
    - [ ] Magazine_Transform (empty, para animar)
    - [ ] Bolt_Transform (empty, para animar)

### **3. Animator Controllers**

#### Player Hands:
- [ ] `Assets/Animations/Controllers/AR_Hands.controller`
  - [ ] Parameter: `Reload` (Trigger)
  - [ ] State: `Idle_AR_Hands` (default)
  - [ ] State: `Reload_AR_Hands`
  - [ ] Transition: Any State → Reload_AR_Hands (condition: Reload)
  - [ ] Transition: Reload_AR_Hands → Idle_AR_Hands (exit time: 1.0)

#### Weapon:
- [ ] `Assets/Animations/Controllers/TalonAR_Weapon.controller`
  - [ ] Parameter: `WeaponReload` (Trigger)
  - [ ] State: `IdleTalon_AR` (default)
  - [ ] State: `ReloadTalon_AR`
  - [ ] Transition: Any State → ReloadTalon_AR (condition: WeaponReload)
  - [ ] Transition: ReloadTalon_AR → IdleTalon_AR (exit time: 1.0)

### **4. Animation Clips (Placeholder)**

Crear vacíos por ahora (animar después):
- [ ] `Assets/Animations/Hands/Idle_AR_Hands.anim`
- [ ] `Assets/Animations/Hands/Reload_AR_Hands.anim` (2.5s)
- [ ] `Assets/Animations/Weapons/IdleTalon_AR.anim`
- [ ] `Assets/Animations/Weapons/ReloadTalon_AR_Magazine.anim` (2.5s)

---

## ⚙️ SCENE SETUP

### **PlayerPrefab (TeamA y TeamB)**

- [ ] **WeaponHolder** (empty child)
  - Position: `(0.2, 0.5, 0.3)` aprox (ajustar)
  
- [ ] **RightHandGrip** (empty child)
  - Position: `(0.15, 0.4, 0.25)` aprox
  
- [ ] **LeftHandGrip** (empty child)
  - Position: `(0.05, 0.35, 0.4)` aprox
  
- [ ] **mixamorig:Spine2**
  - [ ] Rig Builder component (ya debe tener)
  - [ ] Animator component (agregar si no tiene)
    - Controller: `AR_Hands.controller`
  
- [ ] **PlayerSetup component**
  - [ ] Weapon Holder: Asignar WeaponHolder
  - [ ] Right Hand Grip: Asignar RightHandGrip
  - [ ] Left Hand Grip: Asignar LeftHandGrip
  - [ ] Rig Transform: Asignar mixamorig:Spine2
  - [ ] Hands Animator: Asignar mixamorig:Spine2 (mismo GameObject)

### **GameScene Canvas**

- [ ] **FireButton** (UI Button)
  - Name: `"FireButton"` (exacto)
  - Position: Bottom-right corner
  - Size: 100x100 px
  - Text: "🔥" o ícono de disparo

---

## 🔧 CONFIGURACIÓN FINAL

### **TalonAR.asset Inspector**
- [ ] Weapon Prefab: Arrastra `TalonAR_Prefab`
- [ ] Fire Sound: Arrastra AudioClip (opcional, puede ser null por ahora)
- [ ] Hands Animator Controller: Arrastra `AR_Hands.controller`
- [ ] Position Offset: Ajustar según modelo (ej: `0.15, -0.1, 0.3`)
- [ ] Right/Left Hand Grip Positions: Temporalmente `(0,0,0)` y `(0,0,-0.2)`

### **TalonAR_Prefab Inspector**
- [ ] Animator → Controller: `TalonAR_Weapon.controller`
- [ ] FireWeapon → Fire Point: Arrastra `FirePoint`
- [ ] AudioSource configurado (puede estar vacío)

---

## 🧪 TESTING ORDER

### **Test 1: Backend Connection**
```
1. Play en Unity
2. Login con usuario que tenga talon_ar
3. Console debe mostrar:
   ✅ "Primary: talon_ar - default"
```

### **Test 2: Weapon Spawn**
```
1. En Lobby, Create Room
2. Enter GameScene
3. Console debe mostrar:
   ✅ "Weapon spawned and initialized: TALON-AR"
4. Scene Hierarchy debe tener:
   WeaponHolder → TalonAR_Prefab(Clone)
```

### **Test 3: Animator Connection**
```
1. En GameScene
2. Console debe mostrar:
   ✅ "Hands animator: Connected"
   ✅ "Weapon animator: Connected"
3. Inspector → PlayerPrefab → Spine2 → Animator
   Debe tener "AR_Hands" controller
```

### **Test 4: FireButton Connection**
```
1. En GameScene
2. Console debe mostrar:
   ✅ "FireButton found and connected"
3. Click FireButton
4. Console debe mostrar:
   ✅ "Fired TALON-AR"
   ✅ "Applied recoil: ..."
```

### **Test 5: Reload Triggers (Temporal con R key)**
```
1. Agrega código temp en FireWeapon.Update():
   if (Input.GetKeyDown(KeyCode.R)) StartReload();
2. Press R
3. Console debe mostrar:
   ✅ "Player hands reload triggered"
   ✅ "Weapon reload triggered"
   ✅ "Reload started, duration: 2.5s"
   (2.5s después)
   ✅ "Reload complete"
```

---

## 🚨 COMMON MISTAKES

- ❌ `TalonAR.asset` en carpeta incorrecta (debe ser `Resources/Weapons/`)
- ❌ WeaponId typo: `"TalonAR"` vs `"talon_ar"` (debe ser minúsculas con underscore)
- ❌ FireButton con nombre diferente (debe ser exacto: `"FireButton"`)
- ❌ Animator en PlayerPrefab sin controller asignado
- ❌ PlayerSetup sin referencias asignadas
- ❌ Weapon prefab sin Animator component
- ❌ Parameters mal escritos: `"Reload"` vs `"WeaponReload"` (diferentes!)

---

## 📋 COMPLETION CRITERIA

**TalonAR está listo cuando:**

```
✅ Login → Console: "Primary: talon_ar"
✅ Lobby → UI: "TALON-AR // 1"
✅ GameScene → Console: "Weapon spawned: TALON-AR"
✅ GameScene → Console: "Hands animator: Connected"
✅ GameScene → Console: "Weapon animator: Connected"
✅ GameScene → Console: "FireButton found"
✅ Click Fire → Console: "Fired TALON-AR"
✅ Press R → Console: ambos "reload triggered"
✅ Scene → TalonAR visible en WeaponHolder
✅ Movement con joystick funciona
✅ Camera look con touch funciona
```

**Si todos esos checks pasan → TalonAR setup completo ✅**

---

## 🎯 NEXT: Animation Phase

Una vez que todos los checks pasen:

1. Animar weapon parts (magazine, bolt) en `ReloadTalon_AR_Magazine.anim`
2. Animar hands en `Reload_AR_Hands.anim` siguiendo el arma
3. Ajustar IK grip positions para que manos agarren correctamente
4. Iterar position offsets hasta que se vea bien en Game View
5. Polish: muzzle flash, bullet tracers, hit effects

**Después de eso → Bolt (secondary weapon) será mucho más rápido**

---

## 📚 REFERENCIAS

- [WEAPON_SYSTEM_SETUP.md](WEAPON_SYSTEM_SETUP.md) - Setup detallado general
- [ANIMATOR_CONTROLLER_SETUP.md](ANIMATOR_CONTROLLER_SETUP.md) - Animators guía completa
- [TALON_AR_FLOW_TEST.md](TALON_AR_FLOW_TEST.md) - Testing detallado y troubleshooting

---

**Recuerda**: Una arma 100% funcional > dos armas 50% funcionales. Enfócate en TalonAR primero.
