using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtisansGuns.Auth
{
    /// <summary>
    /// Dispatches callbacks to the Unity main thread.
    /// Needed for Google Sign-In which completes on a thread pool thread.
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static UnityMainThreadDispatcher _instance;
        private static readonly Queue<Action> _queue = new Queue<Action>();

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            lock (_queue)
            {
                while (_queue.Count > 0)
                    _queue.Dequeue()?.Invoke();
            }
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue)
            {
                _queue.Enqueue(action);
            }
        }
    }
}
