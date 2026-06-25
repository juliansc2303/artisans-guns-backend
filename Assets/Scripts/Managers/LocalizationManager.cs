using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtisansGuns.Managers
{
    public static class LocalizationManager
    {
        public enum Language { EN, ES }

        public static Language CurrentLanguage { get; private set; } = Language.ES;

        /// <summary>Fired when the language changes. All UI controllers should re-render.</summary>
        public static event Action OnLanguageChanged;

        private static readonly Dictionary<string, string> esTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ═══════════════════════════════════════
            // NAVIGATION
            // ═══════════════════════════════════════
            { "HOME", "INICIO" },
            { "WEAPONS", "ARMAS" },
            { "AGENTS", "SKINS" },
            { "SHOP", "TIENDA" },
            { "HATS", "SOMBREROS" },
            { "LOBBY", "SALA" },
            { "ROOM", "SALA" },
            { "BACK", "ATRÁS" },
            { "◄ BACK", "◄ ATRÁS" },

            // ═══════════════════════════════════════
            // LOADING / CONNECTION
            // ═══════════════════════════════════════
            { "CONNECTING...", "CONECTANDO..." },
            { "WAITING FOR SERVER", "ESPERANDO SERVIDOR" },
            { "INITIALIZING SESSION", "INICIANDO SESIÓN" },
            { "LOADING...", "CARGANDO..." },
            { "LOADING WEAPONS...", "CARGANDO ARMAS..." },
            { "LOADING ABILITIES...", "CARGANDO HABILIDADES..." },
            { "LOADING SOUNDS...", "CARGANDO SONIDOS..." },
            { "READY", "LISTO" },
            { "WAITING FOR SERVER...", "ESPERANDO SERVIDOR..." },
            { "PLEASE WAIT", "POR FAVOR ESPERA" },
            { "RECONNECTING...", "RECONECTANDO..." },
            { "CONNECTING TO SERVER", "CONECTANDO AL SERVIDOR" },
            { "CONNECTING", "CONECTANDO" },
            { "CONNECTION FAILED", "CONEXIÓN FALLIDA" },
            { "TAP TO RETRY", "TOCA PARA REINTENTAR" },
            { "RETRY", "REINTENTAR" },

            // ═══════════════════════════════════════
            // LOBBY / ROOMS
            // ═══════════════════════════════════════
            { "TEAM DEATHMATCH", "TODOS CONTRA TODOS" },
            { "START GAME", "JUGAR" },
            { "CREATE / JOIN", "CREAR / UNIRSE" },
            { "ROOMS", "SALAS" },
            { "No rooms available. Create one!", "No hay salas disponibles. ¡Crea una!" },
            { "Players", "Jugadores" },
            { "FULL", "LLENA" },
            { "JOIN ▶", "UNIRSE ▶" },
            { "JOINING ROOM...", "UNIÉNDOSE A SALA..." },
            { "JOINING...", "UNIÉNDOSE..." },
            { "WAITING FOR SERVER DATA", "ESPERANDO DATOS DEL SERVIDOR" },
            { "SEARCHING MATCH...", "BUSCANDO PARTIDA..." },
            { "CREATING ROOM...", "CREANDO SALA..." },
            { "SETTING UP SERVER", "CONFIGURANDO SERVIDOR" },
            { "CREATING PRIVATE ROOM...", "CREANDO SALA PRIVADA..." },
            { "GENERATING CODE", "GENERANDO CÓDIGO" },
            { "PRIVATE ROOM CREATED", "SALA PRIVADA CREADA" },
            { "CUSTOM GAME", "PARTIDA PERSONALIZADA" },
            { "CREATE A ROOM", "CREAR UNA SALA" },
            { "GAMEMODE", "MODO DE JUEGO" },
            { "MAP", "MAPA" },
            { "CREATE ROOM", "CREAR SALA" },
            { "JOIN WITH CODE", "UNIRSE CON CÓDIGO" },
            { "Enter a room code to join any game", "Ingresa un código de sala para unirte" },
            { "JOIN", "UNIRSE" },
            { "AVAILABLE ROOMS", "SALAS DISPONIBLES" },
            { "ROOM CODE", "CÓDIGO DE SALA" },

            // ═══════════════════════════════════════
            // ROOM / TEAM
            // ═══════════════════════════════════════
            { "TEAM A", "EQUIPO A" },
            { "TEAM B", "EQUIPO B" },
            { "TEAM ALPHA", "EQUIPO ALPHA" },
            { "TEAM BRAVO", "EQUIPO BRAVO" },
            // "READY" already defined above in Loading section
            { "WAITING FOR MISSION START...", "ESPERANDO INICIO DE MISIÓN..." },
            { "GAME STARTING", "JUEGO INICIANDO" },
            { "PREPARE FOR BATTLE", "PREPÁRATE PARA LA BATALLA" },

            // ═══════════════════════════════════════
            // WEAPONS TAB
            // ═══════════════════════════════════════
            { "ARSENAL", "ARSENAL" },
            { "ARSENAL // LOADOUT CUSTOMIZATION", "ARSENAL // PERSONALIZAR EQUIPAMIENTO" },
            { "PRIMARY", "PRIMARIA" },
            { "SECONDARY", "SECUNDARIA" },
            { "KNIFE", "CUCHILLO" },
            { "PRIMARY WEAPONS", "ARMAS PRIMARIAS" },
            { "SECONDARY WEAPONS", "ARMAS SECUNDARIAS" },
            { "KNIFE SKINS", "SKINS DE CUCHILLO" },
            { "WEAPON SKINS", "SKINS DE ARMA" },
            { "SKINS", "SKINS" },
            { "LOCK IN", "EQUIPAR" },
            { "SELECT", "SELECCIONAR" },
            { "EQUIP", "EQUIPAR" },

            // ═══════════════════════════════════════
            // AGENTS TAB
            // ═══════════════════════════════════════
            { "SELECT YOUR AGENT", "SELECCIONA TU AGENTE" },
            { "SELECTED", "SELECCIONADO" },
            { "SELECT AGENT", "SELECCIONAR AGENTE" },
            { "NONE", "NINGUNO" },

            // ═══════════════════════════════════════
            // SHOP TAB
            // ═══════════════════════════════════════
            { "OWNED", "ADQUIRIDO" },
            { "SELECT AN ITEM", "SELECCIONA UN ARTÍCULO" },
            { "BUY", "COMPRAR" },
            { "NOT ENOUGH", "INSUFICIENTE" },
            { "PURCHASING...", "COMPRANDO..." },

            // ═══════════════════════════════════════
            // HATS TAB
            // ═══════════════════════════════════════
            { "EQUIPPED", "EQUIPADO" },
            { "LOCKED", "BLOQUEADO" },
            { "SELECT A HAT", "SELECCIONA UN GORRO" },
            { "UNEQUIP", "DESEQUIPAR" },

            // ═══════════════════════════════════════
            // GAMEPLAY HUD
            // ═══════════════════════════════════════
            { "KILLS", "BAJAS" },
            { "GO!", "¡YA!" },
            { "MATCH STARTING", "PARTIDA INICIANDO" },
            { "GET READY...", "PREPÁRATE..." },
            { "WAITING FOR PLAYERS...", "ESPERANDO JUGADORES..." },
            { "SCORES", "PUNTAJE" },
            { "SCOREBOARD", "MARCADOR" },
            { "MATCH OVER", "PARTIDA TERMINADA" },
            { "DRAW", "EMPATE" },
            { "Victory", "Victoria" },
            { "Defeat", "Derrota" },
            { "JOINED THE ROOM", "SE UNIÓ A LA SALA" },
            { "LEFT THE ROOM", "SALIÓ DE LA SALA" },
            { "A PLAYER", "UN JUGADOR" },
            { "LEVEL UP!", "¡SUBISTE DE NIVEL!" },
            { "Coins", "Monedas" },
            { "Diamonds!", "¡Diamantes!" },

            // ═══════════════════════════════════════
            // SETTINGS
            // ═══════════════════════════════════════
            { "SETTINGS", "AJUSTES" },
            { "GENERAL", "GENERAL" },
            { "Sensitivity", "Sensibilidad" },
            { "Mouse Sensitivity", "Sensibilidad" },
            { "Render Shadows", "Renderizar Sombras" },
            { "AUDIO", "AUDIO" },
            { "Music Volume", "Volumen de Música" },
            { "CONTROLS", "CONTROLES" },
            { "Fire Button Side", "Lado del Botón de Disparo" },
            { "LEFT", "IZQUIERDA" },
            { "RIGHT", "DERECHA" },
            { "EXIT", "SALIR" },
            { "EXIT MATCH?", "¿SALIR DE LA PARTIDA?" },
            { "You won't receive rewards if you leave now.", "No recibirás recompensas si sales ahora." },
            { "LEAVE", "SALIR" },
            { "STAY", "QUEDARSE" },
            { "LOGOUT", "CERRAR SESIÓN" },
            { "LANGUAGE", "IDIOMA" },

            // ═══════════════════════════════════════
            // AUTH / LOGIN
            // ═══════════════════════════════════════
            { "LOGIN", "INICIAR SESIÓN" },
            { "PLAY", "JUGAR" },
            { "NEW ACCOUNT", "NUEVA CUENTA" },
            { "PICK YOUR RIVAL", "ELIGE TU RIVAL" },
            { "Register", "Registrarse" },
            { "USERNAME", "USUARIO" },
            { "PASSWORD", "CONTRASEÑA" },
            { "REPEAT PASSWORD", "REPETIR CONTRASEÑA" },
            { "PLAYER NAME", "NOMBRE DEL JUGADOR" },
            { "CREATE", "CREAR" },
            { "✓ READY!", "✓ ¡LISTO!" },
            { "Account created!", "¡Cuenta creada!" },
            { "LET'S GO", "¡VAMOS!" },
            { "INITIATING SESSION...", "INICIANDO SESIÓN..." },
            { "CONNECTING TO SERVER (MAY TAKE UP TO 120S)", "CONECTANDO AL SERVIDOR (PUEDE TOMAR HASTA 120S)" },
            { "SETTING UP SESSION", "CONFIGURANDO SESIÓN" },
            { "SETTING UP USER", "CONFIGURANDO USUARIO" },
            { "CREATING ACCOUNT...", "CREANDO CUENTA..." },
            { "Username is required", "El usuario es requerido" },
            { "Password is required", "La contraseña es requerida" },
            { "Username must be between 3 and 50 characters", "El usuario debe tener entre 3 y 50 caracteres" },
            { "Password must be at least 6 characters", "La contraseña debe tener al menos 6 caracteres" },
            { "Passwords do not match", "Las contraseñas no coinciden" },
            { "Character name must be between 3 and 20 characters", "El nombre debe tener entre 3 y 20 caracteres" },
            { "Character name cannot be only spaces", "El nombre no puede ser solo espacios" },
            { "Character name can only contain letters, numbers and spaces", "El nombre solo puede contener letras, números y espacios" },

            // ═══════════════════════════════════════
            // GOOGLE / ACCOUNT LINKING
            // ═══════════════════════════════════════
            { "Sign up with Google & earn", "Regístrate con Google y gana" },
            { "GET STARTED", "COMENZAR" },
            { "LINK YOUR ACCOUNT", "VINCULA TU CUENTA" },
            { "Sign up & earn", "Regístrate y gana" },
            { "Sign up with Google", "Registrarse con Google" },
            { "Already have an account?", "¿Ya tienes una cuenta?" },
            { "Sign in with Google", "Iniciar sesión con Google" },
            { "CHOOSE YOUR NAME", "ELIGE TU NOMBRE" },
            { "Choose a unique character name (3-18 characters).", "Elige un nombre único (3-18 caracteres)." },
            { "CHARACTER NAME", "NOMBRE DEL PERSONAJE" },
            { "CONFIRM", "CONFIRMAR" },
            { "ACCOUNT LINKED!", "¡CUENTA VINCULADA!" },
            { "You earned a bonus reward", "Ganaste una recompensa extra" },
            { "RIVAL COINS", "MONEDAS RIVAL" },
            { "CLAIM", "RECLAMAR" },
            { "WELCOME BACK", "BIENVENIDO DE VUELTA" },
            { "Log in to restore your saved progress, weapons, and agents.", "Inicia sesión para restaurar tu progreso, armas y agentes." },
            { "This will replace your current guest progress with your saved account.", "Esto reemplazará tu progreso de invitado con tu cuenta guardada." },

            // ═══════════════════════════════════════
            // PLAYER CARD
            // ═══════════════════════════════════════
            { "Player", "Jugador" },
            { "IDENT // CHARACTER", "IDENT // PERSONAJE" },
            { "✓ READY", "✓ LISTO" },

            // ═══════════════════════════════════════
            // MISC
            // ═══════════════════════════════════════
            { "DEFAULT", "DEFAULT" },
            { "MISSION COMMENCING", "MISIÓN COMENZANDO" },
            { "LOADING DATA...", "CARGANDO DATOS..." },
            { "SESSION READY", "SESIÓN LISTA" },
            { "LEAVING ROOM...", "SALIENDO DE SALA..." },
            { "DISCONNECTING", "DESCONECTANDO" },
            { "ROOM ID //", "ID SALA //" },
            { "MODE //", "MODO //" },
            { "CAPACITY //", "CAPACIDAD //" },
            { "READY ✓", "LISTO ✓" },
            { "HOST", "ANFITRIÓN" },
            { "LVL", "NVL" },
            { "Name must be 3-18 characters", "El nombre debe tener entre 3 y 18 caracteres" },
            { "Only letters, numbers and spaces allowed", "Solo se permiten letras, números y espacios" },
            { "Google sign-in expired. Please try again.", "Inicio de sesión de Google expiró. Inténtalo de nuevo." },
            { "Google Sign-In not available", "Inicio de sesión de Google no disponible" },
            { "CODE:", "CÓDIGO:" },
            { "XP", "XP" },
            { "CURRENT:", "ACTUAL:" },
            { "STARTING IN", "COMIENZA EN" },
            { "K", "K" },
            { "D", "M" },
            { "HS", "TC" },
            { "STR", "RAC" },
        };

        /// <summary>
        /// Translate a string to the current language.
        /// Returns the original string for EN, or the translation for ES.
        /// Weapon names, character names, and skin names are NOT in the dictionary
        /// and will pass through unchanged.
        /// </summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            if (CurrentLanguage == Language.EN) return english;
            return esTranslations.TryGetValue(english, out var translated) ? translated : english;
        }

        /// <summary>
        /// Initialize from saved preference. Call once at app startup.
        /// </summary>
        public static void Initialize()
        {
            string saved = PlayerPrefs.GetString("language", "es");
            CurrentLanguage = saved == "en" ? Language.EN : Language.ES;
        }

        /// <summary>
        /// Switch language and persist the choice.
        /// </summary>
        public static void SetLanguage(Language lang)
        {
            if (CurrentLanguage == lang) return;
            CurrentLanguage = lang;
            PlayerPrefs.SetString("language", lang == Language.EN ? "en" : "es");
            PlayerPrefs.Save();
            OnLanguageChanged?.Invoke();
        }
    }
}
