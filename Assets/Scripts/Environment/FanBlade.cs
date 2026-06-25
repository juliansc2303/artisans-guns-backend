using UnityEngine;

namespace ArtisansGuns.Environment
{
    /// <summary>
    /// Rota las aspas de un ventilador decorativo y reproduce su sonido ambiental.
    /// Agregar este componente al GameObject de las aspas.
    /// </summary>
    public class FanBlade : MonoBehaviour
    {
        [Header("Rotación")]
        [Tooltip("Velocidad de giro en grados por segundo")]
        [SerializeField] private float rotationSpeed = 360f;

        [Tooltip("Eje local de rotación (Y = arriba por defecto)")]
        [SerializeField] private Vector3 rotationAxis = Vector3.up;

        [Header("Sonido")]
        [Tooltip("Clip de audio del ventilador (loop)")]
        [SerializeField] private AudioClip fanSound;

        [Tooltip("Volumen del sonido (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.5f;

        [Tooltip("Distancia máxima a la que se escucha el ventilador")]
        [SerializeField] private float maxDistance = 15f;

        private AudioSource audioSource;

        private void Awake()
        {
            if (fanSound != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = fanSound;
                audioSource.loop = true;
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f; // 3D
                audioSource.volume = volume;
                audioSource.minDistance = 1f;
                audioSource.maxDistance = maxDistance;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        private void Start()
        {
            if (audioSource != null)
                audioSource.Play();
        }

        private void Update()
        {
            transform.Rotate(rotationAxis.normalized, rotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}
