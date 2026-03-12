using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ArtisansGuns.Auth
{
    /// <summary>
    /// Wrapper around Google Sign-In SDK.
    /// Handles the Google Sign-In popup and returns the ID token for backend verification.
    /// </summary>
    public class GoogleAuthService : MonoBehaviour
    {
        public static GoogleAuthService Instance { get; private set; }

        // Web Client ID from google-services.json (type 3 = web)
        private const string WEB_CLIENT_ID = "329775748159-oj54pn4q1l2e13khrkfk76105117q18n.apps.googleusercontent.com";

        public event Action<string> OnGoogleSignInSuccess;  // passes ID token
        public event Action<string> OnGoogleSignInFailed;    // passes error message

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private bool _configured = false;

        /// <summary>
        /// Launch the Google Sign-In popup. On success fires OnGoogleSignInSuccess with the ID token.
        /// In Editor: simulates sign-in with a test token so you can test the full flow without building.
        /// </summary>
        public void SignIn()
        {
#if UNITY_ANDROID || UNITY_IOS
            // Set configuration once — re-creating it every call can put the SDK in a bad state
            if (!_configured)
            {
                var configuration = new Google.GoogleSignInConfiguration
                {
                    WebClientId = WEB_CLIENT_ID,
                    RequestIdToken = true
                };
                Google.GoogleSignIn.Configuration = configuration;
                Google.GoogleSignIn.Configuration.UseGameSignIn = false;
                Google.GoogleSignIn.Configuration.RequestIdToken = true;
                _configured = true;
            }

            // Always sign out first to clear any cached session.
            // Without this, a second SignIn() call after a successful one
            // returns a stale/hanging Task because the SDK thinks it's already signed in.
            try { Google.GoogleSignIn.DefaultInstance.SignOut(); }
            catch (System.Exception) { /* ignore — SDK may not be initialized yet */ }

            Debug.Log("[GoogleAuth] Starting Google Sign-In...");

            try
            {
                Google.GoogleSignIn.DefaultInstance.SignIn().ContinueWith(task =>
                {
                    // Task completes on a thread pool thread — bounce back to main thread
                    UnityMainThreadDispatcher.Enqueue(() => HandleSignInResult(task));
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GoogleAuth] SignIn() threw synchronously: {ex.Message}");
                OnGoogleSignInFailed?.Invoke($"Google Sign-In error: {ex.Message}");
            }
#else
            Debug.LogWarning("[GoogleAuth] Google Sign-In is only supported on Android/iOS.");
            OnGoogleSignInFailed?.Invoke("Google Sign-In is only available on Android and iOS.");
#endif
        }

#if UNITY_ANDROID || UNITY_IOS
        private void HandleSignInResult(Task<Google.GoogleSignInUser> task)
        {
            if (task.IsFaulted)
            {
                string err = task.Exception?.InnerException?.Message ?? "Unknown error";
                // Extract GoogleSignIn status code for better diagnostics
                var innerEx = task.Exception?.InnerException;
                if (innerEx is Google.GoogleSignIn.SignInException signInEx)
                {
                    err = $"GoogleSignIn error: Status={signInEx.Status} ({(int)signInEx.Status})";
                    Debug.LogError($"[GoogleAuth] SignInException Status: {signInEx.Status} ({(int)signInEx.Status})");
                }
                Debug.LogError($"[GoogleAuth] Sign-in failed: {err}");
                OnGoogleSignInFailed?.Invoke(err);
            }
            else if (task.IsCanceled)
            {
                Debug.Log("[GoogleAuth] Sign-in cancelled by user");
                OnGoogleSignInFailed?.Invoke("Sign-in cancelled");
            }
            else
            {
                var user = task.Result;
                string idToken = user.IdToken;
                if (string.IsNullOrEmpty(idToken))
                {
                    Debug.LogError("[GoogleAuth] Sign-in succeeded but no ID token received");
                    OnGoogleSignInFailed?.Invoke("No ID token received from Google");
                    return;
                }
                Debug.Log($"[GoogleAuth] Sign-in success: {user.DisplayName}");
                OnGoogleSignInSuccess?.Invoke(idToken);
            }
        }
#endif

        public void SignOut()
        {
#if UNITY_ANDROID || UNITY_IOS
            Google.GoogleSignIn.DefaultInstance.SignOut();
            Debug.Log("[GoogleAuth] Signed out of Google");
#endif
        }

        /// <summary>
        /// Disconnect revokes access and forces the account picker to appear on the next SignIn().
        /// Use this on logout so the user can pick a different account.
        /// </summary>
        public void Disconnect()
        {
#if UNITY_ANDROID || UNITY_IOS
            Google.GoogleSignIn.DefaultInstance.Disconnect();
            Debug.Log("[GoogleAuth] Disconnected from Google (will show account picker on next sign-in)");
#endif
        }
    }
}
