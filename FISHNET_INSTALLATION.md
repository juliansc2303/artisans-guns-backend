# Instalación Manual de FishNet

## Problema Encontrado
El método de instalación via Git URL falló debido a que el tag 4.7.4 no existe en el repositorio.

---

## Solución: Instalación Manual via Unity Package

### Opción 1: Descarga desde GitHub (Recomendado)

1. **Ir a GitHub Releases:**
   - URL: https://github.com/FirstGearGames/FishNet/releases
   - O buscar directamente: "FishNet Unity Releases"

2. **Descargar el .unitypackage:**
   - Buscar la versión más reciente (ej: 4.4.5 o superior)
   - Click en "Assets" de la release
   - Descargar: `FishNet.Release.unitypackage` o `FishNet.[version].Release.unitypackage`
   - Guardar en tu carpeta Downloads

3. **Importar en Unity:**
   - Abrir Unity Editor
   - Assets > Import Package > Custom Package...
   - Navegar a Downloads
   - Seleccionar: FishNet.Release.unitypackage
   - Click "Open"
   - En la ventana de Import:
     - Verificar que TODO esté marcado
     - Click "Import"
   - Esperar 2-3 minutos a que compile

---

### Opción 2: Asset Store (Alternativa)

1. **Abrir Asset Store en Unity:**
   - Window > Asset Store
   - O ir a: https://assetstore.unity.com

2. **Buscar FishNet:**
   - Escribir "Fish-Net Networking"
   - Seleccionar el package (es GRATIS)

3. **Agregar a cuenta:**
   - Click "Add to My Assets"
   - Iniciar sesión si es necesario

4. **Importar:**
   - En Unity: Window > Package Manager
   - Dropdown: "My Assets"
   - Buscar: Fish-Net
   - Click "Download" → "Import"

---

### Opción 3: Git URL Corregida (Si funciona)

Si quieres intentar con Git de nuevo, usa esta URL en manifest.json:

```json
"com.firstgeargames.fishnet": "https://github.com/FirstGearGames/FishNet.git?path=/Assets/FishNet"
```

(Sin especificar versión - usa la rama principal)

Pasos:
1. Cerrar Unity
2. Editar: Packages/manifest.json
3. Agregar la línea arriba en dependencies
4. Guardar
5. Abrir Unity
6. Esperar a que descargue

---

## Verificar Instalación Exitosa

Después de importar, verificar:

1. **En Project Window:**
   - Debe aparecer carpeta: `Assets/FishNet/`
   - Subcarpetas: Runtime, Plugins, etc.

2. **En Package Manager:**
   - Window > Package Manager
   - Debe aparecer "Fish-Net" en la lista

3. **Probar creando NetworkManager:**
   - GameObject > Create Empty
   - Add Component > "Network Manager"
   - Si aparece el componente = instalación exitosa ✅

---

## Siguiente Paso Después de Instalar

Una vez que FishNet esté instalado correctamente:

1. Seguir la guía: `UNITY_NETWORKING_SETUP_GUIDE.md`
2. Comenzar por crear Bootstrap scene
3. Configurar NetworkManager
4. Testing básico

---

## Problemas Comunes

### "Network Manager component not found"
- FishNet no está instalado correctamente
- Reimportar el package
- Verificar que la carpeta Assets/FishNet existe

### Errores de compilación después de importar
- Esperar 2-3 minutos a que Unity termine de compilar
- Si persisten: Assets > Reimport All

### Package Manager muestra error Git
- Usar método de descarga manual (.unitypackage)
- Es más confiable que Git URL

---

## Enlaces Útiles

- **GitHub Releases**: https://github.com/FirstGearGames/FishNet/releases
- **Documentación**: https://fish-networking.gitbook.io/docs/
- **Discord Support**: https://discord.gg/Ta9HgDh4Hj
- **Unity Forums**: Buscar "Fish-Net Networking"

---

## Notas

- FishNet es completamente GRATIS
- No requiere cuenta ni licencia
- Funciona en todas las plataformas (PC, Mobile, WebGL)
- Es uno de los mejores networking solutions para Unity

---

**Una vez instalado, avísame y continuamos con la configuración de escenas.** 🎮
