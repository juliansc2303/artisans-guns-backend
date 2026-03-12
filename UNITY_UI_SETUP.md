# Configuración UI Unity - Sistema de Autenticación

## Estado Actual
✅ Backend corriendo en http://localhost:3000
✅ Scripts de Unity creados (AuthManager, AuthUIManager)
✅ Escenas creadas (LoginScene, LobbyScene)

## Pasos para Configurar LoginScene

### 1. Abrir LoginScene
1. En Unity, ve a **Assets/Scenes/**
2. Doble clic en **LoginScene.unity**

### 2. Crear Estructura de UI

#### A. Canvas Principal
1. **Hierarchy → Click derecho → UI → Canvas**
2. Renombrar a "AuthCanvas"
3. En Inspector, configurar:
   - Canvas Scaler → UI Scale Mode: **Scale With Screen Size**
   - Reference Resolution: **1920 x 1080**

#### B. EventSystem (si no existe)
- Hierarchy → Click derecho → UI → Event System

#### C. RegisterPanel
1. Hierarchy → Click derecho en AuthCanvas → UI → Panel
2. Renombrar a "RegisterPanel"
3. Agregar **4 Input Fields**:
   - Click derecho en RegisterPanel → UI → Input Field - TextMeshPro (o Legacy)
   - Nombres: `UsernameInput`, `PasswordInput`, `RepeatPasswordInput`, `CharacterNameInput`
   - Configurar cada uno con placeholder text apropiado
   - PasswordInput y RepeatPasswordInput: marcar **Content Type → Password**

4. Agregar **Text** para título:
   - UI → Text - TextMeshPro
   - Nombre: `TitleText`
   - Texto: "Create Account"

5. Agregar **Text** para errores:
   - UI → Text - TextMeshPro
   - Nombre: `ErrorText`
   - Color: Rojo
   - Texto: "" (vacío)

6. Agregar **2 Buttons**:
   - UI → Button - TextMeshPro
   - Nombres: `CreateAccountButton` (texto: "Create Account"), `GoToLoginButton` (texto: "Already have account? Login")

#### D. LoginPanel
1. Hierarchy → Click derecho en AuthCanvas → UI → Panel
2. Renombrar a "LoginPanel"
3. Agregar **2 Input Fields**:
   - `UsernameInput`, `PasswordInput`
   - PasswordInput: Content Type → Password

4. Agregar **Text** para título: "Login"
5. Agregar **Text** para errores (rojo, vacío)
6. Agregar **2 Buttons**:
   - `LoginButton` (texto: "Login")
   - `GoToRegisterButton` (texto: "Create Account")

#### E. SuccessPanel
1. Hierarchy → Click derecho en AuthCanvas → UI → Panel
2. Renombrar a "SuccessPanel"
3. Agregar **Text**: `MessageText`
4. Agregar **Button**: `ContinueButton` (texto: "Continue")

#### F. LoadingPanel
1. Hierarchy → Click derecho en AuthCanvas → UI → Panel
2. Renombrar a "LoadingPanel"
3. Agregar **Text**: "Loading..."
4. Configurar fondo semi-transparente (negro con alpha 0.8)

### 3. Configurar AuthManager

1. **Hierarchy → Click derecho → Create Empty**
2. Renombrar a "AuthManager"
3. Inspector → Add Component → **AuthManager** (script)
4. Configurar:
   - **Backend URL**: `http://localhost:3000/api`

### 4. Configurar AuthUIManager

1. **Hierarchy → Click derecho → Create Empty**
2. Renombrar a "AuthUIManager"
3. Inspector → Add Component → **AuthUIManager** (script)
4. **Arrastrar referencias**:
   
   **RegisterPanel Section:**
   - registerPanel → RegisterPanel GameObject
   - usernameInputRegister → RegisterPanel/UsernameInput
   - passwordInputRegister → RegisterPanel/PasswordInput
   - repeatPasswordInput → RegisterPanel/RepeatPasswordInput
   - characterNameInput → RegisterPanel/CharacterNameInput
   - errorTextRegister → RegisterPanel/ErrorText
   - createAccountButton → RegisterPanel/CreateAccountButton
   - goToLoginButton → RegisterPanel/GoToLoginButton
   
   **LoginPanel Section:**
   - loginPanel → LoginPanel GameObject
   - usernameInputLogin → LoginPanel/UsernameInput
   - passwordInputLogin → LoginPanel/PasswordInput
   - errorTextLogin → LoginPanel/ErrorText
   - loginButton → LoginPanel/LoginButton
   - goToRegisterButton → LoginPanel/GoToRegisterButton
   
   **SuccessPanel Section:**
   - successPanel → SuccessPanel GameObject
   - successMessageText → SuccessPanel/MessageText
   - continueButton → SuccessPanel/ContinueButton
   
   **LoadingPanel Section:**
   - loadingPanel → LoadingPanel GameObject
   
   **Settings:**
   - lobbySceneName: `LobbyScene`

### 5. Probar en Unity Editor

1. Presiona **Play**
2. Deberías ver el RegisterPanel
3. Llena los campos:
   - Username: `testuser`
   - Password: `123456`
   - Repeat Password: `123456`
   - Character Name: `TestHero`
4. Click en "Create Account"
5. Si funciona, verás "Account created!" y te llevará al Login
6. Login con las mismas credenciales
7. Debería cargar LobbyScene

### 6. Build Settings

1. **File → Build Settings**
2. Agregar escenas en orden:
   - 0: LoginScene
   - 1: LobbyScene
3. Click "Add Open Scenes"

## Verificación Rápida

Abre la consola de Unity (Ctrl + Shift + C) y busca:
- ✅ `✅ Registration successful`
- ✅ `✅ Login successful`
- ❌ Si ves errores HTTP, verifica que el backend esté corriendo

## Siguiente Paso: Testing Mobile

Una vez funcionando en Unity Editor, continuar con build Android:
1. File → Build Settings → Android
2. Switch Platform
3. Player Settings → Other Settings:
   - Package Name: `com.artesanogames.artisansguns`
   - Minimum API Level: 24
4. Build APK
5. Cambiar AuthManager Backend URL a: `http://[TU_IP_PC]:3000/api`
6. Instalar en Android y probar

## Backend URL por Entorno

- **Unity Editor**: `http://localhost:3000/api`
- **Android (mismo WiFi)**: `http://192.168.X.X:3000/api` (usar tu IP de PC)
- **Producción**: `https://tu-dominio.com/api`
