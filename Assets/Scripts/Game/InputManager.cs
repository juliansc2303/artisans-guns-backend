using UnityEngine;
using Fusion;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// InputManager - Recopila input del jugador y lo envÃ­a a Fusion
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        private NetworkRunner runner;

        private void Start()
        {
            runner = FindObjectOfType<NetworkRunner>();
        }

        private void Update()
        {
            if (runner == null || !runner.IsRunning)
                return;

            // Get local player controller
            var localPlayer = GetLocalPlayer();
            if (localPlayer == null)
                return;

            // Gather input
            Vector2 moveInput = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")
            );

            bool jumpInput = Input.GetKeyDown(KeyCode.Space);

            // Send to player controller
            localPlayer.SetInput(moveInput, jumpInput);
        }

        private PlayerController GetLocalPlayer()
        {
            var players = FindObjectsOfType<PlayerController>();
            foreach (var player in players)
            {
                if (player.HasInputAuthority)
                {
                    return player;
                }
            }
            return null;
        }
    }
}
