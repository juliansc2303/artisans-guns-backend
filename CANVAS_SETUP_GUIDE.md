# 🎨 GUÍA COMPLETA: CONFIGURACIÓN UI CANVAS
## Artisans Guns Dos - Sistema Canvas con Sprites Personalizados

---

## 📋 RESUMEN
Esta guía te llevará paso a paso para configurar el nuevo sistema de UI basado en **Unity Canvas** usando tus sprites personalizados (bg, charcard, panel1, panel2, button1-3, roompanel, header).

**Scripts creados:**
- ✅ `Assets/Scripts/UI/Canvas/LobbyCanvasController.cs`
- ✅ `Assets/Scripts/UI/Canvas/RoomCanvasController.cs`
- ✅ `Assets/Scripts/UI/Canvas/RoomItemUI.cs`
- ✅ `Assets/Scripts/UI/Canvas/PlayerItemUI.cs`

---

## 🎯 PARTE 1: CONFIGURAR SPRITES

### 1.1 Configurar Sprite Mode
Para TODOS los sprites en `Assets/UI/Sprites/`:

1. Selecciona cada sprite en el Project window
2. En el Inspector, configura:
   - **Texture Type:** `Sprite (2D and UI)`
   - **Sprite Mode:** `Single`
   - **Pixels Per Unit:** `100`
   - **Filter Mode:** `Bilinear`
   - **Mesh Type:** `Tight`
   - Marca **Generate Mip Maps** como `false`
3. Click **Apply**

### 1.2 Configurar Sprite Border (para paneles y botones)
Para sprites que se estiran (charcard, panel1, panel2, button1-3, roompanel, header):

1. Selecciona el sprite
2. En el Inspector, click en **Sprite Editor**
3. Arrastra los bordes verdes HACIA ADENTRO (define qué parte NO se estira):
   - **Panel/Card sprites:** ~20-30 píxeles desde cada borde
   - **Button sprites:** ~15-25 píxeles desde cada borde
   - **Header:** ~20 píxeles arriba/abajo, ~40 píxeles a los lados
4. Click **Apply** y cierra el Sprite Editor

**Sprites que necesitan border:**
- ✅ charcard.png (bordes dorados)
- ✅ panel1.png (marco superior)
- ✅ panel2.png (panel de rooms)
- ✅ button1.png, button2.png, button3.png
- ✅ roompanel.png
- ✅ header.png

**Sprites sin border:**
- ❌ bg.png (background completo)

---

## 🏗️ PARTE 2: CREAR LOBBY CANVAS

### 2.1 Crear estructura base
1. En la escena **LobbyScene**, crear:
   ```
   Canvas (Lobby)
   ├── Background (Image)
   ├── Header (Image)
   ├── CharacterPanel (Image)
   ├── RoomsPanel (Image)
   ├── SettingsOverlay (GameObject - con panel hijo)
   ├── CharacterSelectOverlay (GameObject - con panel hijo)
   └── CreateRoomOverlay (GameObject - con panel hijo)
   ```

2. Selecciona el **Canvas**:
   - Canvas Scaler:
     - **UI Scale Mode:** `Scale With Screen Size`
     - **Reference Resolution:** `1920 x 1080`
     - **Screen Match Mode:** `Match Width Or Height`
     - **Match:** `0.5` (balancea entre ancho y alto)
   - **Render Mode:** `Screen Space - Overlay`

### 2.2 Configurar Background
```
Background (Image)
├── Anchor: Stretch both (Alt+Shift + click stretch all)
├── Left/Right/Top/Bottom: 0, 0, 0, 0
├── Source Image: bg.png
├── Image Type: Simple
├── Preserve Aspect: false
```

### 2.3 Configurar Header
```
Header (Image)
├── Anchor: Top Center
├── Pos X: 0, Pos Y: -100
├── Width: 1600, Height: 180
├── Source Image: header.png
├── Image Type: Sliced (IMPORTANTE)
└── Hijos:
    ├── TitleText (TextMeshPro)
    │   └── Text: "LOBBY"
    └── SettingsButton (Button)
        └── Image: button1.png (Sliced)
```

### 2.4 Configurar CharacterPanel (Izquierda)
```
CharacterPanel (Image)
├── Anchor: Left Center
├── Pos X: 300, Pos Y: 0
├── Width: 450, Height: 800
├── Source Image: charcard.png
├── Image Type: Sliced
└── Hijos:
    ├── TitleText (TMP): "YOUR CHARACTER"
    ├── CharacterNameText (TMP): "CRIMSON"
    ├── CharacterIcon (Image): 👤 o sprite del personaje
    └── ChangeCharacterButton (Button)
        ├── Image: button2.png (Sliced)
        └── Text (TMP): "CHANGE CHARACTER"
```

### 2.5 Configurar RoomsPanel (Derecha)
```
RoomsPanel (Image)
├── Anchor: Right Center
├── Pos X: -300, Pos Y: 0
├── Width: 900, Height: 800
├── Source Image: panel2.png
├── Image Type: Sliced
└── Hijos:
    ├── Header (Horizontal Layout)
    │   ├── TitleText (TMP): "AVAILABLE ROOMS"
    │   └── RefreshButton (Button)
    ├── RoomListContainer (Vertical Layout Group)
    │   └── ScrollRect aquí
    └── CreateRoomButton (Button)
        ├── Image: button3.png (Sliced)
        └── Text (TMP): "+ CREATE ROOM"
```

**Configurar ScrollRect:**
```
RoomListContainer (GameObject)
└── ScrollRect component
    ├── Content: crear GameObject "Content" con Vertical Layout Group
    ├── Viewport: crear Image transparente como máscara
    ├── Vertical Scrollbar: opcional
    └── Scroll Sensitivity: 30
```

---

## 🏗️ PARTE 3: CREAR ROOM ITEM PREFAB

1. Crear GameObject vacío en Hierarchy: "RoomItemPrefab"
2. Estructura:
```
RoomItemPrefab (Image - roompanel.png Sliced)
├── RoomNameText (TextMeshPro)
├── PlayerCountText (TextMeshPro)
├── MapNameText (TextMeshPro)
└── JoinButton (Button)
    ├── Image: button3.png (Sliced)
    └── JoinButtonText (TMP): "JOIN"
```

3. Agregar componente **RoomItemUI.cs**
4. Asignar referencias en el Inspector:
   - Room Name Text → RoomNameText
   - Player Count Text → PlayerCountText
   - Map Name Text → MapNameText
   - Join Button → JoinButton
   - Join Button Text → JoinButtonText

5. **Configurar Layout:**
   - RoomItemPrefab:
     - Add Component: **Horizontal Layout Group**
     - Padding: 20, 20, 15, 15
     - Spacing: 15
     - Child Alignment: Middle Left
     - Child Force Expand: false
   - Add Component: **Layout Element**
     - Preferred Height: 120

6. Drag RoomItemPrefab a `Assets/UI/Canvas/Prefabs/` para crear el prefab
7. Elimina de Hierarchy

---

## 🏗️ PARTE 4: CREAR PLAYER ITEM PREFAB

1. Crear GameObject: "PlayerItemPrefab"
2. Estructura:
```
PlayerItemPrefab (Image - panel1.png Sliced con tint sutil)
├── LocalPlayerHighlight (Image - opcional, verde transparente)
├── UsernameText (TextMeshPro)
├── CharacterText (TextMeshPro)
├── HostBadge (Image con TMP "HOST")
└── ReadyBadge (Image con TMP "READY")
```

3. Agregar componente **PlayerItemUI.cs**
4. Asignar referencias
5. **Configurar Layout:**
   - Add Component: **Horizontal Layout Group**
   - Padding: 15, 15, 10, 10
   - Add Component: **Layout Element**
     - Preferred Height: 90

6. Drag a `Assets/UI/Canvas/Prefabs/`
7. Elimina de Hierarchy

---

## 🏗️ PARTE 5: CREAR ROOM CANVAS

### 5.1 Estructura Room Scene
En **RoomScene**, crear Canvas similar a Lobby:
```
Canvas (Room)
├── Background (Image - bg.png)
├── Header (Image - header.png Sliced)
│   ├── RoomNameText (TMP)
│   ├── MapNameText (TMP)
│   └── LeaveButton (Button - button1.png)
├── TeamAPanel (Image - panel1.png Sliced)
│   ├── TitleText (TMP): "TEAM A"
│   └── TeamAListContainer (ScrollRect)
├── TeamBPanel (Image - panel1.png Sliced, con tint naranja)
│   ├── TitleText (TMP): "TEAM B"
│   └── TeamBListContainer (ScrollRect)
├── BottomControls
│   ├── HostControls (GameObject)
│   │   └── StartGameButton (Button - button3.png)
│   ├── WaitingMessage (TMP)
│   └── ReadyButton (Button - button2.png)
└── CountdownOverlay (GameObject - fondo negro 90% alpha)
    ├── CountdownText (TMP - font size 240)
    └── CountdownMessageText (TMP): "GET READY!"
```

### 5.2 Configurar Layouts
- TeamAPanel y TeamBPanel:
  - Anchor: Left/Right Center
  - TeamA: Pos X: 300
  - TeamB: Pos X: -300
  - Width: 550, Height: 700
  
- ScrollRects con Content → Vertical Layout Group
  - Spacing: 10
  - Child Force Expand Width: true

---

## 🔧 PARTE 6: CONECTAR SCRIPTS

### 6.1 LobbyScene
1. Selecciona Canvas
2. Add Component: **LobbyCanvasController**
3. Asigna TODAS las referencias (usa el Inspector, drag & drop):
   - Character Panel → CharacterPanel GameObject
   - Rooms Panel → RoomsPanel GameObject
   - Header Panel → Header GameObject
   - Character Name Text → el TMP_Text del nombre
   - Change Character Button → el botón
   - Room List Container → el Content del ScrollRect
   - Room Item Prefab → drag desde Assets/UI/Canvas/Prefabs/RoomItemPrefab
   - Create Room Button → el botón
   - Todos los overlays y sus elementos

### 6.2 RoomScene
1. Selecciona Canvas
2. Add Component: **RoomCanvasController**
3. Asigna referencias:
   - Room Name Text, Map Name Text, Leave Button
   - Team A List Container, Team B List Container
   - Player Item Prefab → drag desde Assets/UI/Canvas/Prefabs/PlayerItemPrefab
   - Host Controls, Waiting Message
   - Start Game Button, Ready Button
   - Countdown Overlay, Countdown Text

---

## 🎨 PARTE 7: CONFIGURAR TEXTMESHPRO

### 7.1 Importar TMP Essentials
Si no lo has hecho:
1. Window → TextMeshPro → Import TMP Essential Resources

### 7.2 Crear estilo de texto personalizado
Para títulos grandes (header "LOBBY", "AVAILABLE ROOMS"):
```
Font: Bold
Font Size: 48-60
Color: Dorado (#FFD700) o blanco (#FFFFFF)
Outline:
  - Width: 0.2
  - Color: Negro semi-transparente
Glow (opcional):
  - Inner: 0.3
  - Outer: 0.2
```

Para texto normal:
```
Font Size: 24-28
Color: Blanco o dorado claro
```

Para botones:
```
Font Size: 22-26
Font Style: Bold
Letter Spacing: 2-4
Color: Blanco
```

---

## ⚙️ PARTE 8: CONFIGURAR BUTTONS

### 8.1 Transiciones de botones
Para TODOS los botones:
1. Selecciona el Button component
2. **Transition:** `Color Tint`
3. Configurar colores:
   - **Normal Color:** Blanco (1, 1, 1, 1)
   - **Highlighted Color:** Amarillo claro (1, 1, 0.8, 1)
   - **Pressed Color:** Amarillo oscuro (0.9, 0.9, 0.7, 1)
   - **Disabled Color:** Gris (0.6, 0.6, 0.6, 0.5)
4. **Fade Duration:** 0.1

### 8.2 Navigation
- **Navigation:** `Automatic` o `None` (dependiendo de si quieres control con teclado)

---

## 🧪 PARTE 9: TESTING

### 9.1 Checklist Lobby
- [ ] Background se ve correctamente
- [ ] Header con título "LOBBY"
- [ ] CharacterPanel a la izquierda con nombre de personaje
- [ ] RoomsPanel a la derecha
- [ ] Botón "Create Room" funciona
- [ ] Botón "Change Character" abre overlay
- [ ] Settings button funciona

### 9.2 Checklist Room
- [ ] Header con nombre de sala y mapa
- [ ] Team A panel a la izquierda
- [ ] Team B panel a la derecha
- [ ] Jugadores se distribuyen en equipos
- [ ] Host ve botón "START GAME"
- [ ] Otros jugadores ven botón "READY"
- [ ] Countdown overlay aparece al iniciar
- [ ] Transición a GameScene funciona

---

## 🐛 TROUBLESHOOTING

### Problema: Sprites se ven estirados/pixelados
**Solución:** 
- Verifica Texture Type = Sprite (2D and UI)
- Verifica Image Type = Sliced (para paneles/botones)
- Configura Sprite Border en Sprite Editor

### Problema: Botones no responden
**Solución:**
- Verifica que el Canvas tiene un **EventSystem** en la escena
- Verifica que el Button tiene un **Graphic Raycaster**
- Verifica que la Image del botón tiene **Raycast Target** marcado

### Problema: ScrollRect no funciona
**Solución:**
- Verifica que Content tiene **Rect Transform** más grande que Viewport
- Verifica que hay un **Vertical Layout Group** en Content
- Verifica **Movement Type** = Elastic o Clamped

### Problema: TextMeshPro no aparece
**Solución:**
- Importa TMP Essential Resources
- Verifica que Font Asset está asignado
- Verifica color del texto (no transparente)

### Problema: Referencias null en scripts
**Solución:**
- Verifica que TODOS los campos SerializeField están asignados en Inspector
- Usa Debug.Log para identificar qué referencia falta
- Verifica nombres de GameObjects (case-sensitive)

---

## 📝 NOTAS IMPORTANTES

1. **Orden de render:**
   - Background debe tener el Order in Layer más bajo
   - Overlays deben tener Order más alto o estar al final en Hierarchy

2. **Performance:**
   - Usa **Sprite Atlas** para combinar sprites y reducir draw calls
   - Deshabilita Raycast Target en Images que no son botones
   - Usa Object Pooling para listas de jugadores/rooms (opcional)

3. **Responsive Design:**
   - Canvas Scaler maneja diferentes resoluciones
   - Usa Anchors correctamente para paneles adaptables
   - Prueba en diferentes aspect ratios (16:9, 16:10, 9:16 móvil)

4. **Sprites vs UIToolkit:**
   - ✅ Canvas soporta gradientes (en sprites)
   - ✅ Canvas soporta efectos visuales complejos
   - ✅ Canvas tiene mejor control sobre layout
   - ✅ Canvas es más estándar en industria

---

## ✅ CHECKLIST FINAL

Antes de testear:
- [ ] Todos los sprites configurados (Sprite Mode, Border)
- [ ] Canvas Scaler configurado (1920x1080, Match 0.5)
- [ ] LobbyCanvas con LobbyCanvasController
- [ ] RoomCanvas con RoomCanvasController
- [ ] RoomItemPrefab con RoomItemUI
- [ ] PlayerItemPrefab con PlayerItemUI
- [ ] Todas las referencias asignadas en Inspector
- [ ] TextMeshPro configurado con estilos
- [ ] Buttons con Color Tint transitions
- [ ] EventSystem en ambas escenas

---

## 🎉 RESULTADO FINAL

Deberías tener:
- ✅ UI profesional con tus sprites personalizados
- ✅ Layout responsive adaptable a diferentes pantallas
- ✅ Funcionalidad completa de lobby y room
- ✅ Efectos visuales mejorados vs UIToolkit
- ✅ Sistema escalable para futuras mejoras

**¡Listo para agregar más efectos visuales, animaciones, y polish!**
