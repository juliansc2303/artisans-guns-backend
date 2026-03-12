# Cambios para Sistema Optimizado de Spawn

##  Resumen
Separar `PlayerDataPrefab` (lobby) de `PlayerPrefab` (juego) para optimizar network y rendimiento.

## 1. Crear PlayerDataPrefab

1. En Unity, **duplica** el PlayerPrefab actual
2. Renómbralo a `PlayerData`
3. **Elimina** todos estos componentes (solo dejar NetworkObject + PlayerNetworkData):
   - ❌ Model 3D (Crimson_Vibe_Sight_Pato)
   - ❌ Camera
   - ❌ PlayerController
   - ❌ PlayerSetup
   - ❌ GameUIManager
   - ❌ FireWeapon / WeaponRecoil / WeaponSway
   - ❌ Animator
   - ❌ RigBuilder
   - ❌ Colliders / Rigidbody

4. **Debe quedar solo:**
   ```
   PlayerData (GameObject)
   ├─ NetworkObject
   └─ PlayerNetworkData
   ```

## 2. Asignar en NetworkManager Inspector

1. Abre `NetworkManager` en la escena Lobby
2. En el Inspector, verás:
   - **Player Prefab** → Asigna `PlayerPrefab` (el completo, ya lo tienes)
   - **Player Data Prefab** → Asigna `PlayerData` (el nuevo que creaste)
   - **Network Runner Prefab** → Ya lo tienes
   - **Game State Manager Prefab** → Ya lo tienes

## 3. Modificar NetworkManager.cs - SpawnPlayer()

Reemplaza el método `SpawnPlayer` (línea ~1027) con esto:

```csharp
private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
{
    string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    bool isGameScene = sceneName == "Sandbox" || sceneName.StartsWith("Map");
    
    // Choose correct prefab: PlayerData for lobby, full Player for game
    NetworkObject prefabToSpawn = isGameScene ? playerPrefab : playerDataPrefab;
    
    if (prefabToSpawn == null)
    {
        return;
    }

    Vector3 spawnPosition;
    
    if (isGameScene)
    {
        // GAME SCENE: Spawn full PlayerPrefab at team spawn point
        var gameManager = FindObjectOfType<ArtisansGuns.Game.GameManager>();
        
        if (gameManager != null)
        {
            // Get existing PlayerNetworkData from Room to determine team
            var existingPlayerData = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd.Object != null && pd.Object.InputAuthority == player);
            
            int playerTeam = existingPlayerData != null && existingPlayerData.TeamAssigned 
                ? existingPlayerData.Team 
                : 0;
            
            spawnPosition = gameManager.GetSpawnPositionForTeam(playerTeam);
        }
        else
        {
            spawnPosition = new Vector3(0f, 1f, 0f);
        }
    }
    else
    {
        // LOBBY/ROOM: Spawn lightweight PlayerData at origin (data sync only)
        spawnPosition = Vector3.zero;
    }

    var playerObject = runner.Spawn(prefabToSpawn, spawnPosition, Quaternion.identity, player);

    if (playerObject != null)
    {
        OnPlayerJoinedRoom?.Invoke(player, playerObject);
    }
}
```

## 4. Flujo Final

### En Room/Lobby:
- Se spawnea `PlayerData` (solo datos: nombre, agent, team, ready, loadout)
- Ligero, solo NetworkObject + PlayerNetworkData
- NO renderiza nada visual

### Al entrar a Sandbox:
- Se spawnea `PlayerPrefab` completo (modelo 3D, armas, cámara, todo)
- Lee el `PlayerNetworkData` existente para saber:
  - Qué team
  - Qué character
  - Qué armas (primary/secondary)
  - Username
- Se posiciona en el spawn point correcto de su team

## 5. Ventajas

✅ **Optimización**: No cargas modelos 3D en lobby  
✅ **Network**: Menos datos sincronizados en Room  
✅ **Escalabilidad**: Puedes tener 10+ jugadores en lobby sin lag  
✅ **Arquitectura**: Separación clara entre datos y representación visual  
✅ **Valorant-style**: Mismo patrón que juegos AAA

## 6. Testing

1. Entra a Lobby → Verifica que NO veas modelos 3D (solo UI actualizado)
2. Crea Room → Verifica que los PlayerCards funcionen
3. Start Game → Verifica que spawne correctamente en Sandbox con modelo 3D
4. Cambia armas en loadout → Verifica que se guarden en PlayerNetworkData
5. Reconecta → Verifica que mantenga los datos
