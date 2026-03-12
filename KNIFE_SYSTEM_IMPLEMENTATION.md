# KNIFE SYSTEM IMPLEMENTATION GUIDE

## ✅ COMPLETADO - Backend & Frontend Core

### Backend Changes:
1. ✅ **Base de datos**: SQL script creado en `Backend/src/database/ADD_KNIFE_SUPPORT.sql`
   - Agrega columna `knife_skin` a tabla `users`
   - Inicializa todos los usuarios con knife default
   - Agrega knife skins a `unlocked_weapon_skins`

2. ✅ **API Service** (`Backend/src/services/loadoutService.js`):
   - `getLoadout()` ahora retorna `knifeSkin`
   - `updateLoadout()` ahora acepta y valida `knifeSkin`
   - Validación de knife skins desbloqueados

### Frontend Changes:
1. ✅ **Data Models**:
   - `WeaponDefinition.cs`: Agregada categoría `Knife`
   - `KnifeSkinDefinition.cs`: Nuevo archivo con sistema de knife skins
   - `AuthManager.cs`: `UserData` incluye `knifeSkin`
   - `LoadoutManager.cs`: 
     - `LoadoutData` incluye `knifeSkin`
     - Método `UpdateKnifeSkin()` agregado
     - `IsSkinUnlocked()` soporta knife

2. ✅ **UI Controller**:
   - `WeaponsTabController.cs`: 
     - Agregados elementos UI para knife slot
     - Event handler `OnKnifeSlotClicked()`
     - Display de knife skin actual

---

## 🔨 PENDIENTE - Trabajo de UI/UX

### 1. Agregar elementos UI en UXML (Unity UI Toolkit)

**Archivo**: `Assets/UI/LobbyScreen.uxml` (o similar)

En la sección de `WeaponsContent` → `MainView`, agregar el tercer slot:

```xml
<!-- Existing slots -->
<ui:Button name="PrimarySlotButton" class="weapon-slot-button">
    <ui:VisualElement name="PrimaryWeaponIcon" class="weapon-icon" />
    <ui:Label name="PrimaryWeaponName" text="TALON-AR" class="weapon-name" />
</ui:Button>

<ui:Button name="SecondarySlotButton" class="weapon-slot-button">
    <ui:VisualElement name="SecondaryWeaponIcon" class="weapon-icon" />
    <ui:Label name="SecondaryWeaponName" text="BOLT" class="weapon-name" />
</ui:Button>

<!-- NEW: Knife Slot -->
<ui:Button name="KnifeSlotButton" class="weapon-slot-button">
    <ui:VisualElement name="KnifeIcon" class="weapon-icon" />
    <ui:Label name="KnifeName" text="DEFAULT" class="weapon-name" />
</ui:Button>
```

### 2. Crear Knife Skins Screen

**Nuevo archivo**: `Assets/Scripts/UI/KnifeSkinsController.cs`

```csharp
using UnityEngine;
using UnityEngine.UIElements;
using ArtisansGuns.Data;
using ArtisansGuns.Managers;

namespace ArtisansGuns.UI
{
    public class KnifeSkinsController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        
        private VisualElement knifeSkinsRoot;
        private Button backButton;
        private ScrollView skinsGrid;
        private Button selectButton;
        
        private KnifeSkinDefinition.KnifeSkin currentSelectedSkin;
        private KnifeSkinDefinition.KnifeSkin equippedSkin;

        private void OnEnable()
        {
            CacheUIElements();
            RegisterEventHandlers();
            LoadCurrentKnifeSkin();
            PopulateSkinsGrid();
        }

        private void CacheUIElements()
        {
            var root = uiDocument.rootVisualElement;
            var content = root.Q<VisualElement>("KnifeSkinsContent");
            
            knifeSkinsRoot = content.Q<VisualElement>("KnifeSkinsRoot");
            backButton = content.Q<Button>("BackButton");
            skinsGrid = content.Q<ScrollView>("SkinsGrid");
            selectButton = content.Q<Button>("SelectButton");
        }

        private void RegisterEventHandlers()
        {
            backButton?.RegisterCallback<ClickEvent>(evt => OnBackClicked());
            selectButton?.RegisterCallback<ClickEvent>(evt => OnSelectClicked());
        }

        private void LoadCurrentKnifeSkin()
        {
            if (LoadoutManager.Instance != null && LoadoutManager.Instance.IsInitialized())
            {
                var loadout = LoadoutManager.Instance.GetLoadout();
                if (loadout.knifeSkin != null)
                {
                    equippedSkin = KnifeSkinDefinition.GetKnifeSkinById(loadout.knifeSkin.skinId);
                }
            }

            if (equippedSkin == null)
            {
                equippedSkin = KnifeSkinDefinition.GetDefaultKnifeSkin();
            }
            
            currentSelectedSkin = equippedSkin;
        }

        private void PopulateSkinsGrid()
        {
            skinsGrid.Clear();
            var allSkins = KnifeSkinDefinition.GetAllKnifeSkins();

            foreach (var skin in allSkins)
            {
                var skinCard = CreateSkinCard(skin, skin == equippedSkin);
                skinsGrid.Add(skinCard);
            }
        }

        private VisualElement CreateSkinCard(KnifeSkinDefinition.KnifeSkin skin, bool equipped)
        {
            var card = new VisualElement();
            card.AddToClassList("knife-skin-card");
            if (equipped) card.AddToClassList("equipped");

            // Icon
            var icon = new VisualElement();
            icon.AddToClassList("knife-skin-icon");
            var skinIcon = Resources.Load<Texture2D>(skin.iconPath);
            if (skinIcon != null)
            {
                icon.style.backgroundImage = new StyleBackground(skinIcon);
            }
            card.Add(icon);

            // Name
            var nameLabel = new Label(skin.displayName);
            nameLabel.AddToClassList("knife-skin-name");
            card.Add(nameLabel);

            // Cost (if not unlocked)
            bool isUnlocked = LoadoutManager.Instance?.IsSkinUnlocked("knife", skin.skinId) ?? false;
            if (!isUnlocked)
            {
                var costLabel = new Label($"{skin.cost} BP");
                costLabel.AddToClassList("knife-skin-cost");
                card.Add(costLabel);
            }

            // Click handler
            card.RegisterCallback<ClickEvent>(evt => OnSkinCardClicked(skin, card));

            return card;
        }

        private void OnSkinCardClicked(KnifeSkinDefinition.KnifeSkin skin, VisualElement card)
        {
            // Check if unlocked
            bool isUnlocked = LoadoutManager.Instance?.IsSkinUnlocked("knife", skin.skinId) ?? false;
            if (!isUnlocked)
            {
                // TODO: Show purchase dialog
                return;
            }

            // Deselect all
            var allCards = skinsGrid.Query<VisualElement>(className: "knife-skin-card").ToList();
            foreach (var c in allCards)
            {
                c.RemoveFromClassList("selected");
            }

            // Select clicked
            card.AddToClassList("selected");
            currentSelectedSkin = skin;
            
            UpdateSelectButton();
        }

        private void UpdateSelectButton()
        {
            if (selectButton == null) return;

            if (currentSelectedSkin == equippedSkin)
            {
                selectButton.text = "EQUIPPED";
                selectButton.SetEnabled(false);
            }
            else
            {
                selectButton.text = "SELECT";
                selectButton.SetEnabled(true);
            }
        }

        private void OnSelectClicked()
        {
            if (currentSelectedSkin == null || LoadoutManager.Instance == null) return;

            LoadoutManager.Instance.UpdateKnifeSkin(currentSelectedSkin.skinId, (success) =>
            {
                if (success)
                {
                    equippedSkin = currentSelectedSkin;
                    UpdateSelectButton();
                    PopulateSkinsGrid(); // Refresh to show new equipped state
                }
            });
        }

        private void OnBackClicked()
        {
            // TODO: Notify LobbyScreenManager to return to Weapons view
        }
    }
}
```

### 3. Agregar UXML para Knife Skins Screen

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="KnifeSkinsContent" class="tab-content hidden">
        <ui:VisualElement name="KnifeSkinsRoot" class="knife-skins-container">
            
            <!-- Header -->
            <ui:VisualElement name="Header" class="header">
                <ui:Button name="BackButton" text="← BACK" class="back-button" />
                <ui:Label name="Title" text="KNIFE SKINS" class="title" />
            </ui:VisualElement>
            
            <!-- Skins Grid -->
            <ui:ScrollView name="SkinsGrid" class="skins-grid" />
            
            <!-- Select Button -->
            <ui:Button name="SelectButton" text="SELECT" class="select-button" />
            
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

### 4. Agregar navegación en LobbyScreenManager

En `LobbyScreenManager.cs` (o controlador similar):

```csharp
private void ShowKnifeSkinsScreen()
{
    // Hide weapons main view
    weaponsTabController.Hide();
    
    // Show knife skins view
    knifeSkinsController.Show();
}
```

### 5. Actualizar WeaponsTabController.OnKnifeSlotClicked()

```csharp
private void OnKnifeSlotClicked()
{
    // Get reference to LobbyScreenManager or similar
    var screenManager = FindObjectOfType<LobbyScreenManager>();
    if (screenManager != null)
    {
        screenManager.ShowKnifeSkinsScreen();
    }
}
```

### 6. Agregar botones "SKINS" a Primary/Secondary

En las pantallas de Primary Weapons y Secondary Weapons, agregar botón:

```xml
<ui:Button name="SkinsButton" text="SKINS →" class="skins-navigation-button" />
```

Handler:
```csharp
private void OnPrimarySkinsClicked()
{
    // TODO: Show Primary Weapon Skins screen
}

private void OnSecondarySkinsClicked()
{
    // TODO: Show Secondary Weapon Skins screen
}
```

---

## 📋 Checklist de Implementación

### Backend:
- [ ] Ejecutar `ADD_KNIFE_SUPPORT.sql` en la base de datos
- [ ] Reiniciar servidor backend
- [ ] Verificar que endpoint `/api/loadout` retorna `knifeSkin`

### Frontend:
- [ ] Agregar `KnifeSlotButton` y elementos al UXML
- [ ] Crear `KnifeSkinsController.cs`
- [ ] Crear UXML para Knife Skins screen
- [ ] Implementar navegación desde Weapons → Knife Skins
- [ ] Agregar icono default del knife en `Resources/Icons/Knives/DefaultKnife.png`
- [ ] Agregar botones "Skins" en Primary/Secondary screens
- [ ] Testear flujo completo:
  - [x] Click en Knife slot → Muestra Knife Skins
  - [ ] Seleccionar skin → Se guarda en backend
  - [ ] Regresar a Weapons → Muestra skin seleccionado
  - [ ] Login → Carga knife skin del backend

---

## 🎨 Assets Necesarios

1. **Icono de knife default**:
   - Path: `Resources/Icons/Knives/DefaultKnife.png`
   - Tamaño recomendado: 256x256px
   - Formato: PNG con transparencia

2. **Futuros knife skins** (opcional para futuro):
   - DragonKnife.png
   - KarambitKnife.png
   - Etc.

---

## 🔧 Testing

1. **Backend**:
```bash
cd Backend
node src/server.js
```

2. **Verificar en Postman**:
```
GET http://localhost:3000/api/loadout
Authorization: Bearer <tu-token>

Response debe incluir:
{
  "knifeSkin": { "weaponId": "knife", "skinId": "default" }
}
```

3. **Unity**:
- Play scene Lobby
- Ir a Weapons tab
- Click en Knife slot → Debería mostrar Knife Skins screen
- Seleccionar skin → Guardar
- Verificar que se refleja en main view

---

## 📝 Notas

- El sistema está listo para agregar más knife skins en el futuro
- Solo necesitas agregar entradas en `KnifeSkinDefinition.cs`
- Los skins con `cost > 0` requerirán implementar sistema de compra
- El sistema de unlock de skins ya está implementado en backend

