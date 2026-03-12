# 🏗️ PERSISTENT UI SYSTEM - Setup Guide
**Sistema de UI Persistente inspirado en Valorant**  
*Header, Currency, Settings permanecen entre escenas - Zero flickering*

---

## 📋 Qué es esto?

Un sistema donde el **header, currency display, y settings** permanecen visibles y consistentes en todas las escenas del juego (Lobby, Room, etc.), exactamente como Valorant.

**Ventajas**:
- ✅ Cero parpadeo al cambiar escenas
- ✅ UI consistente (siempre ves tu dinero, settings, etc.)
- ✅ Panel de amigos se implementa UNA vez, funciona en todo el juego
- ✅ Experiencia profesional y fluída

---

## 🚀 Setup en Unity (5 minutos)

### 1. Crear GameObject en LobbyScene

⚠️ **IMPORTANTE**: PersistentUI se crea en **LobbyScene** (primera escena después del login), NO en LoginScene.

En **LobbyScene**:

1. Click derecho en Hierarchy → `Create Empty`
2. Nombrar: **`PersistentUI`**
3. Agregar componente: **`UI Document`**
4. Agregar componente: **`PersistentUIManager`** (script)

### 2. Configurar UI Document

En el componente **UI Document** del GameObject `PersistentUI`:

1. **Panel Settings**: Asignar tu PanelSettings (el mismo que LobbyScreen/RoomScreen)
2. **Source Asset**: Asignar `Assets/UI/PersistentUI.uxml`
3. **Sort Order**: `100` (para que esté encima de todo)

### 3. Por Qué en LobbyScene?

**PersistentUI se crea en LobbyScene** porque el header solo debe aparecer después de iniciar sesión.

**Flujo correcto:**
- LoginScene → Sin header (solo login/registro)
- LobbyScene → Aquí se crea PersistentUI con `DontDestroyOnLoad`
- RoomScene → PersistentUI ya existe (no se destruye)
- GameScene → PersistentUI continúa persistiendo

Una vez creado en LobbyScene, el objeto **nunca se destruye** gracias a `DontDestroyOnLoad`.

---

## 🎮 Cómo Funciona

### Header Persistente

```
PersistentUI (nunca se destruye)
├── Header completo
│   ├── Botones navegación (LOBBY/ROOM/WEAPONS/AGENTS)
│   ├── Logo centrado (LOBBY o LEAVE)
│   ├── Currency (Rival Essence + Points)
│   └── Settings button
└── Player Count (solo en RoomScene)
```

### Escenas - Solo Contenido Central

**LobbyScene.uxml**: Solo character panel + room list  
**RoomScene.uxml**: Solo teams + map info  

NO tienen header propio - usan el de PersistentUI.

### Navegación Automática

- **En Lobby**: Logo dice "LOBBY", se ve WEAPONS/AGENTS
- **En Room**: Logo dice "LEAVE", se ve ROOM/WEAPONS/AGENTS
- **Player Count**: Solo visible en RoomScene

Todo se actualiza automáticamente con `SceneManager.sceneLoaded`.

---

## 🔧 APIs Disponibles

### Actualizar Player Count (desde RoomUIController)

```csharp
if (PersistentUIManager.Instance != null)
{
    PersistentUIManager.Instance.UpdatePlayerCount(current, max);
}
```

### Actualizar Currency (desde cualquier script)

```csharp
// Save to PlayerPrefs
PlayerPrefs.SetInt("rival_essence", 5000);
PlayerPrefs.SetInt("rival_points", 12000);

// PersistentUI se actualiza automáticamente en cada cambio de escena
```

---

## 📝 Notas Importantes

### ⚠️ PersistentUI debe estar en la PRIMERA escena

Si creas PersistentUI en LobbyScene:
- ❌ Se destruirá al cargar RoomScene
- ❌ Header parpadeará entre escenas

**Solución**: Crear PersistentUI en LoginScene (primera escena del juego).

### ✅ Orden de Carga de Escenas

```
1. LoginScene (sin header, solo login)
2. LobbyScene (crea PersistentUI aquí)
   └─→ PersistentUI (DontDestroyOnLoad)
3. RoomScene (PersistentUI ya existe)
4. GameScene (PersistentUI continúa)
```

PersistentUI sobrevive entre LobbyScene, RoomScene, GameScene, etc.

### 🎯 Panel de Amigos (Futuro)

Cuando implementes panel de amigos:
1. Agregar HTML en `PersistentUI.uxml`
2. Agregar lógica en `PersistentUIManager.cs`
3. **Se verá en TODAS las escenas automáticamente** ✅

---

## 🐛 Troubleshooting

### "El header desaparece al cambiar escena"
→ PersistentUI NO está en LobbyScene. Debe crearse ahí, no en LoginScene ni RoomScene.

### "Veo dos headers (duplicados)"
→ LobbyScene o RoomScene todavía tienen header propio. Eliminar.

### "Player count no se actualiza"
→ Verificar que RoomUIController llama a `PersistentUIManager.Instance.UpdatePlayerCount()`.

### "Logo button no funciona"
→ Verificar que `PersistentUIManager` esté asignado en el GameObject.

---

## ✅ Checklist de Setup

- [ ] Crear GameObject `PersistentUI` en **LobbyScene** (primera escena con header)
- [ ] Agregar componente `UI Document`
- [ ] Asignar `PersistentUI.uxml` al UI Document
- [ ] Asignar PanelSettings al UI Document
- [ ] Sort Order = 100 (encima de todo)
- [ ] Agregar componente `PersistentUIManager`
- [ ] Verificar que LobbyScene NO tenga header propio
- [ ] Verificar que RoomScene NO tenga header propio
- [ ] Test: Ir de Lobby → Room → Header no parpadea ✅

---

## 📚 Archivos del Sistema

```
Assets/
├── UI/
│   ├── PersistentUI.uxml       ← Header HTML
│   ├── PersistentUI.uss        ← Header styles
│   ├── Lobby/
│   │   ├── LobbyScreen.uxml    ← Solo contenido (sin header)
│   │   └── LobbyScreen.uss
│   └── Room/
│       ├── RoomScreen.uxml     ← Solo contenido (sin header)
│       └── RoomScreen.uss
└── Scripts/
    └── UI/
        └── PersistentUIManager.cs  ← Lógica del header persistente
```

---

**Implementado**: Feb 6, 2026  
**Inspiración**: Valorant UI System  
**Status**: ✅ Production Ready
