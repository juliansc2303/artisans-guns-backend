# 🎯 OPCIÓN RÁPIDA: Aspect Ratio 16:9 (RECOMENDADO)

## ⚡ Fast Track (3 minutos para todas las escenas)

### Paso Único: Copiar script a cada escena

1. **LoginScene.unity:**
   - Click derecho en jerarquía → Create Empty
   - Renombra: `_AspectRatioSetup`
   - Drag `SceneAspectRatioSetup` desde Assets/Scripts/UI/
   - **Done!** ✅

2. **Repite para:**
   - RoomScene.unity
   - LobbyScene.unity  
   - GameScene.unity

**Total: 5 minutos max.**

---

## 🧠 Cuál Script Usar

| Caso | Script | Ubicación | Setup |
|------|--------|----------|-------|
| **Quiero todo automático** | SceneAspectRatioSetup | En cualquier GameObject | 30 segundos |
| **Solo Canvas UI** | CanvasAutoScaler | En el Canvas | 20 segundos |
| **Control manual total** | AspectRatioManager | En Camera o Canvas | 20 segundos |

---

## 📊 Resultado Final

Después de aplicar cualquiera de estos scripts:

✅ **Todas las escenas** mantienen 16:9
✅ **Pantallas ultra-wide** ven pillarbox (barras negras a los lados)
✅ **Pantallas más estrechas** ven letterbox (barras negras arriba/abajo)
✅ **Si es 16:9 exacto** → Viewport completo, sin cuadros negros
✅ **Cero impacto en performance**

---

## 🔍 Verificación Rápida

Después de añadir los scripts:

1. Abre Game View
2. Click en **Free Aspect** 
3. Redimensiona la ventana
4. Deberías ver cuadros negros aparecer automáticamente según el tamaño

---

## 📚 Para Más Detalles

Ver documento completo: [`ASPECT_RATIO_16_9_SETUP.md`](ASPECT_RATIO_16_9_SETUP.md)

