# Configuración de LoginScene con UI Toolkit

## ✅ Archivos Creados
- `Assets/UI/Auth/LoginScreen.uxml` - Estructura HTML-like
- `Assets/UI/Auth/LoginScreen.uss` - Estilos CSS-like
- `Assets/Scripts/UI/AuthUIController.cs` - Lógica C#

---

## 📋 Pasos de Configuración en Unity

### 1. **Abrir LoginScene**
- Assets → Scenes → LoginScene
- Doble clic para abrir

### 2. **ELIMINAR UI antigua (UGUI)**
- En Hierarchy, selecciona:
  - Canvas (el viejo)
  - EventSystem
  - Todos los GameObjects de UI antigua
- Delete (no los necesitamos más)

### 3. **Crear UI Document (UI Toolkit)**
1. **Hierarchy → Click derecho → UI Toolkit → UI Document**
2. Renombrar a `AuthUIDocument`

### 4. **Configurar UI Document**
En Inspector del `AuthUIDocument`:

1. **Panel Settings:**
   - Click en `+` para crear nuevo
   - Nombre: `AuthPanelSettings`
   - Guardar en `Assets/UI/`
   - Configurar:
     - **Scale Mode:** `Constant Physical Size`
     - **Reference DPI:** 96
     - **Match:** 0.5

2. **Source Asset (UXML):**
   - Arrastra `LoginScreen.uxml` desde `Assets/UI/Auth/`
   - O click en el círculo y selecciona `LoginScreen`

3. **Verificar:**
   - En Game view deberías ver el panel de login

### 5. **Agregar AuthUIController**
1. **Hierarchy → Click derecho → Create Empty**
2. Renombrar a `AuthUIController`
3. **Inspector → Add Component → AuthUIController**
4. **Configurar:**
   - UI Document: Arrastra `AuthUIDocument` desde Hierarchy
   - Lobby Scene Name: `LobbyScene`

### 6. **Verificar AuthManager existe**
- Debe haber un GameObject `AuthManager` con el script `AuthManager.cs`
- Si no existe:
  - Hierarchy → Create Empty → `AuthManager`
  - Add Component → AuthManager
  - Backend URL: `http://localhost:3000/api`

---

## 🎨 Personalización de Colores/Estilos

### Cambiar color del botón LOGIN:
Edita `LoginScreen.uss` línea ~180:
```css
.btn-primary {
    background-color: rgb(0, 180, 235); /* Cambia estos valores RGB */
    box-shadow: 0 4px 15px rgba(0, 180, 235, 0.4);
}
```

### Cambiar transparencia del panel:
Edita `LoginScreen.uss` línea ~30:
```css
.glass-panel {
    background-color: rgba(80, 70, 60, 0.35); /* Último valor = opacidad */
}
```

### Agregar imagen de fondo personalizada:
1. Importa tu imagen a `Assets/Textures/`
2. Edita `LoginScreen.uss` línea ~5:
```css
.background {
    background-image: url('project://database/Assets/Textures/TU_IMAGEN.png');
}
```

---

## 🔧 Iconos para los Input Fields

### Opción 1: Usar iconos built-in de Unity
El USS ya está configurado con:
```css
.username-icon {
    background-image: resource('Icons/user-icon');
}
```

### Opción 2: Usar tus propios iconos (recomendado)
1. **Importa iconos PNG** (24x24px transparentes) a `Assets/UI/Auth/Icons/`
   - `user-icon.png`
   - `lock-icon.png`
   - `star-icon.png`

2. **Actualiza LoginScreen.uss:**
```css
.username-icon {
    background-image: url('project://database/Assets/UI/Auth/Icons/user-icon.png');
}

.password-icon {
    background-image: url('project://database/Assets/UI/Auth/Icons/lock-icon.png');
}

.character-icon {
    background-image: url('project://database/Assets/UI/Auth/Icons/star-icon.png');
}
```

---

## ▶️ Probar en Play Mode

1. **Asegúrate de que el backend está corriendo:**
   ```powershell
   cd Backend
   npm run dev
   ```

2. **Presiona Play en Unity**

3. **Deberías ver:**
   - Panel glassmorphism con efecto blur
   - Campos de Username y Password
   - Botón LOGIN en cyan brillante
   - Botón REGISTER abajo
   - Animaciones smooth al hacer hover

4. **Probar funcionalidad:**
   - Click en REGISTER
   - Crea una cuenta
   - Debería cambiar a panel de login
   - Login con las credenciales
   - Debería cargar LobbyScene

---

## 🎯 Diferencias vs UGUI (sistema antiguo)

| Aspecto | UGUI (antiguo) | UI Toolkit (nuevo) |
|---------|----------------|-------------------|
| Estructura | Inspector manual | UXML (código) |
| Estilos | Inspector manual | USS (CSS-like) |
| Reutilización | Difícil | Fácil (como componentes web) |
| Performance | Medio | Alto (retained mode) |
| Responsive | Manual | Automático |
| Debugging | Difícil | DevTools-like |

---

## 🐛 Troubleshooting

### "No veo el panel en Game view"
- Verifica que UIDocument tiene el UXML asignado
- Verifica que Panel Settings está configurado

### "Los estilos no se aplican"
- Asegúrate de que el USS está en la primera línea del UXML
- Unity puede tardar en compilar, espera unos segundos

### "Los botones no funcionan"
- Verifica que AuthUIController tiene el UIDocument asignado
- Verifica que AuthManager existe en la escena

### "Los iconos no aparecen"
- Es normal si no has importado imágenes
- Los iconos son opcionales, el diseño funciona sin ellos

---

## 📱 Siguiente Paso: Hacer Responsive

Para que se vea bien en móvil, agregar a `LoginScreen.uss`:

```css
@media (max-width: 500px) {
    .glass-panel {
        width: 90%;
        padding: 30px 20px;
    }
    
    .title {
        font-size: 24px;
    }
}
```

---

## ✨ Extras Opcionales

### Animación de entrada del panel:
Agregar al inicio de `AuthUIController.cs` en `OnEnable()`:
```csharp
loginPanel.style.scale = new Scale(Vector3.zero);
loginPanel.schedule.Execute(() => {
    loginPanel.style.scale = new Scale(Vector3.one);
}).ExecuteLater(100);
```

### Partículas de fondo:
Agregar `ParticleSystem` en Hierarchy para ambiente más dinámico

---

¿Listo para probarlo? Solo sigue los pasos 1-6 y presiona Play! 🚀
