using UnityEngine;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// Lightweight relay attached at runtime to TPV weapon instances.
    /// Animation Events on the TPV weapon's Animator can call these methods
    /// because this MonoBehaviour lives on the same GameObject.
    /// Calls are forwarded to PlayerTPVController which owns the actual audio data.
    /// </summary>
    public class TPVSoundRelay : MonoBehaviour
    {
        private PlayerTPVController controller;

        public void Init(PlayerTPVController tpvController)
        {
            controller = tpvController;
        }

        // ── Called from Animation Events on ReloadTPV animation clips ──
        /// <summary>
        /// Plays reload sound at index from the TPV weapon position in 3D.
        /// Usage: Add Animation Event → Function: PlayTPVReloadSound, Int: 0/1/2...
        /// </summary>
        public void PlayTPVReloadSound(int index)
        {
            if (controller != null)
                controller.PlayTPVReloadSound(index);
        }

        // ── Called from Animation Events on knife Attack TPV animation ──
        /// <summary>
        /// Plays the TPV fire sound (knife swing) at the weapon position.
        /// Usage: Add Animation Event → Function: PlayTPVFireSound
        /// </summary>
        public void PlayTPVFireSound()
        {
            if (controller != null)
                controller.PlayTPVFireSoundPublic();
        }
    }
}
