using UnityEngine;

/// <summary>
/// JoystickDebugHelper - Script temporal para debuggear problemas con el FloatingJoystick
/// Agregar este script al mismo GameObject que tiene el FloatingJoystick
/// </summary>
public class JoystickDebugHelper : MonoBehaviour
{
    private Joystick joystick;
    
    private void Start()
    {
        joystick = GetComponent<Joystick>();
        
        if (joystick != null)
        {
            // Debug.Log($"âœ… [JoystickDebug] Joystick encontrado: {joystick.GetType().Name}");
            // Debug.Log($"   GameObject: {gameObject.name}");
            // Debug.Log($"   Active: {gameObject.activeInHierarchy}");
            // Debug.Log($"   Layer: {LayerMask.LayerToName(gameObject.layer)}");
            
            // Verificar componentes padre (Canvas)
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                // Debug.Log($"   Canvas encontrado: {canvas.gameObject.name}");
                // Debug.Log($"   Canvas RenderMode: {canvas.renderMode}");
                // Debug.Log($"   Canvas enabled: {canvas.enabled}");
            }
            else
            {
                // Debug.LogError("âŒ [JoystickDebug] NO hay Canvas padre - el joystick NO funcionarÃ¡!");
            }
        }
        else
        {
            // Debug.LogError("âŒ [JoystickDebug] Componente Joystick NO encontrado en este GameObject!");
        }
    }
    
    private void Update()
    {
        if (joystick != null)
        {
            // Mostrar input cada segundo si hay movimiento
            if (Time.frameCount % 60 == 0)
            {
                Vector2 input = new Vector2(joystick.Horizontal, joystick.Vertical);
                if (input.magnitude > 0.01f)
                {
                    // Debug.Log($"ðŸ•¹ï¸ [JoystickDebug] Input actual: H={joystick.Horizontal:F2}, V={joystick.Vertical:F2}");
                }
            }
        }
    }
    
    // Detectar cuando el joystick recibe eventos
    private void OnGUI()
    {
        if (joystick == null) return;
        
        // Mostrar info en pantalla
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;
        
        string info = $"Joystick: {joystick.GetType().Name}\n";
        info += $"Active: {gameObject.activeInHierarchy}\n";
        info += $"Input: H={joystick.Horizontal:F2}, V={joystick.Vertical:F2}\n";
        info += $"Direction: {joystick.Direction}";
        
        GUI.Label(new Rect(10, 100, 400, 100), info, style);
    }
}
