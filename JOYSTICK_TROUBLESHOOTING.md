# 🎮 Solución de Problemas - Joystick & Camera

## ✅ Cambios Realizados

### 1. **Background Camera FIX** - Ya no causa conflicto de rotación
**Problema:** La background camera rotaba junto con el player causando efectos visuales extraños.

**Solución:** La background camera ahora es **estática** (no es hijo del player), solo renderiza el fondo negro sin rotar.

```csharp
// Antes: bgCamObj.transform.SetParent(transform); // ❌ Rotaba con player
// Ahora: Posición fija en (0,0,0) sin parent // ✅ Estática
```

---

### 2. **Joystick Auto-Detection Mejorado**
**Cambios:**
- Busca cualquier tipo de `Joystick` (no solo `FloatingJoystick`)
- Logs detallados para debugging:
  ```
  ✅ Joystick encontrado automáticamente: FloatingJoystick
     GameObject: Floating Joystick
     Activo: True
  ```

---

### 3. **Debug Logs para Movimiento**
Ahora muestra en Console cuando el joystick genera input:
```
🕹️ Joystick Input: (0.75, 0.50) (magnitude: 0.90)
```

---

## 🔧 Cómo Configurar el FloatingJoystick en Unity

### **Paso 1: Verificar Jerarquía del Canvas**

Tu Canvas en GameScene debe tener esta estructura:

```
Canvas (Screen Space - Overlay)
├── GameplayHUD (UIDocument)
└── Floating Joystick ← Este debe estar aquí
    ├── Background (Image)
    └── Handle (Image)
```

**IMPORTANTE:** El FloatingJoystick debe estar en el **Canvas**, no dentro del UIDocument.

---

### **Paso 2: Configuración del FloatingJoystick Inspector**

Selecciona el GameObject "Floating Joystick" y verifica:

#### **FloatingJoystick Component:**
- ✅ **Background:** Asignado (debe tener el RectTransform del background)
- ✅ **Handle:** Asignado (debe tener el RectTransform del handle)
- ✅ **Handle Range:** 1.0 (default está bien)
- ✅ **Dead Zone:** 0 (para respuesta inmediata)
- ✅ **Axis Options:** Both (horizontal y vertical)

#### **RectTransform (del Floating Joystick):**
- **Anchors:** Bottom-Left
- **Pivot:** (0.5, 0.5)
- **Position:** X=150, Y=150 (ajustar según prefieras)
- **Width/Height:** 200x200 (o más grande si quieres)

#### **Canvas Group (opcional pero recomendado):**
- **Alpha:** 0.8 (para que sea semi-transparente)
- **Interactable:** ✅ Checked
- **Block Raycasts:** ✅ Checked

---

### **Paso 3: Verificar Canvas Settings**

Selecciona el **Canvas** principal y verifica:

```
Canvas Component:
├── Render Mode: Screen Space - Overlay
├── Pixel Perfect: ❌ Unchecked (para mobile)
├── Sort Order: 0

Canvas Scaler Component:
├── UI Scale Mode: Scale With Screen Size
├── Reference Resolution: 1920 x 1080
├── Screen Match Mode: Match Width Or Height
└── Match: 0.5 (equilibrio entre width/height)
```

---

## 🐛 Debugging - Agregar Script Helper

Si el joystick AÚN no funciona, agrega el script de debug:

1. En Unity, selecciona el GameObject **"Floating Joystick"**
2. Add Component → **JoystickDebugHelper**
3. Presiona Play
4. Mira la **Console** y la **pantalla del Game View**

Verás info en tiempo real:
- ✅ Si el joystick está activo
- ✅ Si el Canvas existe
- ✅ Input actual (Horizontal/Vertical)
- ✅ Dirección del joystick

---

## 💡 Problemas Comunes y Soluciones

### **Problema 1: "Joystick NO encontrado"**
**Causa:** No está en la escena o está desactivado.

**Solución:**
1. Busca en Hierarchy: "Floating Joystick"
2. Si no existe: Project → Assets/Joystick Pack/Prefabs → **Floating Joystick.prefab**
3. Arrástralo al **Canvas** (no al UIDocument)

---

### **Problema 2: "Joystick encontrado pero NO aparece visualmente"**
**Causa:** El FloatingJoystick se oculta al inicio y solo aparece al tocar.

**Solución:**
El FloatingJoystick está diseñado así. Deberías ver:
- Background **OCULTO** al inicio
- Al tocar la **mitad izquierda de la pantalla**, el joystick aparece donde tocas
- Al soltar, desaparece

Esto es correcto para un "Floating Joystick" mobile.

---

### **Problema 3: "Joystick aparece pero el player no se mueve"**
**Verificar en Console:**

Si ves: `🕹️ Joystick Input: (0.75, 0.50)` → El joystick funciona
Si ves: `⚠️ movementJoystick es NULL` → No está asignado

**Solución si es NULL:**
1. Selecciona el **PlayerPrefab** (Assets/Prefabs/PlayerPrefab)
2. En el componente **PlayerController**
3. En `Movement Joystick`:
   - Arrastra el GameObject "Floating Joystick" desde la Hierarchy
   - O déjalo vacío para que se auto-detecte (debe funcionar)

---

### **Problema 4: "CharacterController no se mueve"**
**Verificar:**
1. El PlayerPrefab tiene **CharacterController** component
2. El GameObject está en una posición válida (no atascado en geometría)
3. La velocidad `moveSpeed` no es 0 (default: 5)

**Debug adicional:**
En PlayerController, modifica temporalmente `FixedUpdateNetwork`:

```csharp
// Get movement input from joystick
if (movementJoystick != null)
{
    moveInput = new Vector2(movementJoystick.Horizontal, movementJoystick.Vertical);
    
    if (moveInput.magnitude > 0.01f)
    {
        Debug.Log($"🕹️ Input: {moveInput} | IsGrounded: {isGrounded} | Speed: {moveSpeed}");
    }
}
```

---

## 🎯 Checklist Final

Antes de probar en Unity, verifica:

- [ ] Canvas existe en GameScene
- [ ] Floating Joystick está en Canvas (como hijo directo)
- [ ] Floating Joystick tiene componente FloatingJoystick con Background/Handle asignados
- [ ] Canvas Scaler configurado (Screen Space - Overlay)
- [ ] PlayerPrefab tiene CharacterController
- [ ] PlayerController script está en PlayerPrefab
- [ ] Background camera fija (ya no rota con debug)

---

## 🚀 Prueba en Unity

1. **Presiona Play** en GameScene
2. **Mira Console** para logs de detección:
   ```
   ✅ Joystick encontrado automáticamente: FloatingJoystick
   📷 Background camera created for letterbox/pillarbox (static)
   ```
3. **En Game View:**
   - Toca **lado IZQUIERDO** → Joystick aparece
   - Arrastra → Player se mueve
   - Toca **lado DERECHO** → Cámara rota

---

## 📝 Notas Importantes

**FloatingJoystick vs FixedJoystick:**
- **FloatingJoystick:** Aparece donde tocas, desaparece al soltar (MOBILE)
- **FixedJoystick:** Siempre visible en posición fija (más para debugging)

Si prefieres ver el joystick siempre:
1. Usa **Fixed Joystick.prefab** en lugar de Floating
2. O modifica FloatingJoystick.cs para no ocultar background al inicio

---

## 🔍 Si Nada Funciona

1. **Captura de pantalla** de:
   - Hierarchy completa del Canvas
   - Inspector del "Floating Joystick"
   - Console con los logs
   
2. **Verifica Backend:** El backend local debe estar corriendo (`npm run dev` en Backend/)

3. **Revisa que estés en GameScene** - No en LoginScene o LobbyScene
