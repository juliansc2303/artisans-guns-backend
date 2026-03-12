using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// Singleton UI controller that owns the mobile HUD:
    ///   – Virtual joystick (left half, bottom 60 %)
    ///   – Fire / Reload / Knife buttons
    ///   – Ability buttons with radial cooldown dials
    ///   – Primary / Secondary weapon cells
    ///
    /// Other systems subscribe to the static events below instead of
    /// finding Canvas buttons at runtime.
    /// </summary>
    [DefaultExecutionOrder(-50)]   // initialise before PlayerController etc.
    public class MobileControlsController : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────
        public static MobileControlsController Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────
        [Header("UIDocument")]
        [SerializeField] private UIDocument uiDocument;

        [Header("Button Sprites")]
        [SerializeField] private Sprite fireSprite;
        [SerializeField] private Sprite reloadSprite;
        [SerializeField] private Sprite knifeSprite;
        [SerializeField] private Sprite jumpSprite;
        [SerializeField] private Sprite crouchSprite;
        [SerializeField] private Sprite standSprite;

        // ── Static events subscribed by game systems ───────────────────────
        /// <summary>Fire button pressed (down). Replaced by SetFireOverride while grenade is active.</summary>
        public static event Action OnFireDown;
        /// <summary>Fire button released (up).</summary>
        public static event Action OnFireUp;
        public static event Action OnReload;
        public static event Action OnKnifeSelect;
        public static event Action OnPrimarySelect;
        public static event Action OnSecondarySelect;
        public static event Action OnAbility1;
        public static event Action OnAbility2;
        public static event Action OnUltimate;
        // Jump / Crouch / Stand
        public static event Action OnJump;
        public static event Action OnCrouch;
        public static event Action OnStand;
        // Weapon drop / pick-up
        public static event Action OnDropWeapon;
        public static event Action OnPickWeapon;

        // ── Joystick state ────────────────────────────────────────────────
        /// <summary>Normalised movement input [-1,1] from the virtual joystick.</summary>
        public Vector2 MoveInput { get; private set; }

        private const float JoystickMaxRadius = 68f;
        private VisualElement _joystickArea;
        private VisualElement _joystickBase;
        private VisualElement _joystickThumb;
        private int _joystickPointerId = -1;
        private Vector2 _joystickBaseCenter; // panel-space centre of the base ring

        // ── Fire-override (grenade hijack) ────────────────────────────────
        private Action _fireOverrideDown;
        private Action _fireOverrideUp;

        // ── Visual element refs ───────────────────────────────────────────
        private VisualElement _fireButton;
        private VisualElement _reloadButton;
        private VisualElement _knifeButton;
        private VisualElement _ability1Button;
        private VisualElement _ability2Button;
        private VisualElement _jumpButton;
        private VisualElement _crouchButton;
        private VisualElement _standButton;
        private VisualElement _ability1Icon;
        private VisualElement _ability2Icon;
        private VisualElement _ultimateButton;
        private VisualElement _ultimateIcon;
        private VisualElement _ultimateDotsContainer;  // holds 5 dot elements
        private readonly VisualElement[] _ultimateDots = new VisualElement[5];
        private VisualElement _primaryIcon;
        private VisualElement _secondaryIcon;
        private Label         _primaryAmmoLabel;
        private Label         _secondaryAmmoLabel;
        private VisualElement _primaryCell;
        private VisualElement _secondaryCell;
        private VisualElement _dropButton;
        private VisualElement _dropIcon;
        private VisualElement _pickButton;
        private Label         _pickLabel;
        private VisualElement _pickIcon;
        private VisualElement _weaponCellsContainer;  // the vertical WeaponCells column

        private CooldownDialElement _dial1;
        private CooldownDialElement _dial2;

        // ─────────────────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[MobileControls]");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileControlsController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (fireSprite   == null) fireSprite   = LoadSprite("Buttons/StrikeButton");
            if (reloadSprite == null) reloadSprite = LoadSprite("Buttons/ReloadButton");
            if (knifeSprite  == null) knifeSprite  = LoadSprite("Buttons/KnifeButton");
            if (jumpSprite   == null) jumpSprite   = LoadSprite("Buttons/JumpButton");
            if (crouchSprite == null) crouchSprite = LoadSprite("Buttons/CrunchButton");
            if (standSprite  == null) standSprite  = LoadSprite("Buttons/StandButton");
        }

        private static Sprite LoadSprite(string resourcePath)
        {
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null) return sprite;
            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Called by <see cref="GameplayHUDController"/> once its UIDocument root is ready.
        /// Binds all mobile-control elements from the shared HUD UXML.
        /// </summary>
        public void InitializeWithRoot(VisualElement root)
        {
            BindElements(root);
            SetupJoystick(root);
            SetupButtons();
            ApplySprites();
            Debug.Log("[MobileControls] Initialised via GameplayHUD root.");
        }

        // ── Element binding ────────────────────────────────────────────────
        private void BindElements(VisualElement root)
        {
            _joystickArea  = root.Q<VisualElement>("JoystickArea");
            _joystickBase  = root.Q<VisualElement>("JoystickBase");
            _joystickThumb = root.Q<VisualElement>("JoystickThumb");

            _fireButton    = root.Q<VisualElement>("FireButton");
            _reloadButton  = root.Q<VisualElement>("ReloadButton");
            _knifeButton    = root.Q<VisualElement>("KnifeButton");
            _ability1Button = root.Q<VisualElement>("Ability1Button");
            _ability2Button = root.Q<VisualElement>("Ability2Button");
            _jumpButton     = root.Q<VisualElement>("JumpButton");
            _crouchButton   = root.Q<VisualElement>("CrouchButton");
            _standButton    = root.Q<VisualElement>("StandButton");
            _ability1Icon   = root.Q<VisualElement>("Ability1Icon");
            _ability2Icon   = root.Q<VisualElement>("Ability2Icon");

            // Ultimate button + dots
            _ultimateButton = root.Q<VisualElement>("UltimateButton");
            _ultimateIcon   = root.Q<VisualElement>("UltimateIcon");
            _ultimateDotsContainer = root.Q<VisualElement>("UltimateDots");

            // Build 5 dot elements programmatically inside the container
            if (_ultimateDotsContainer != null)
            {
                _ultimateDotsContainer.Clear();
                for (int i = 0; i < 5; i++)
                {
                    var dot = new VisualElement();
                    dot.style.width  = 8f;
                    dot.style.height = 8f;
                    dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                        dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4f;
                    dot.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f, 0.8f));
                    dot.style.marginLeft  = 2f;
                    dot.style.marginRight = 2f;
                    dot.pickingMode = PickingMode.Ignore;
                    _ultimateDots[i] = dot;
                    _ultimateDotsContainer.Add(dot);
                }
            }

            _primaryCell    = root.Q<VisualElement>("PrimaryWeaponCell");
            _secondaryCell  = root.Q<VisualElement>("SecondaryWeaponCell");
            _primaryIcon    = root.Q<VisualElement>("PrimaryWeaponIcon");
            _secondaryIcon  = root.Q<VisualElement>("SecondaryWeaponIcon");
            _primaryAmmoLabel   = root.Q<Label>("PrimaryAmmoLabel");
            _secondaryAmmoLabel = root.Q<Label>("SecondaryAmmoLabel");

            // The vertical column that holds pick → drop → slot row
            _weaponCellsContainer = root.Q<VisualElement>("WeaponCells");
            var slotRow = root.Q<VisualElement>("WeaponSlotRow");

            // Drop / Pick buttons — inserted ABOVE the slot row inside the column
            _dropButton = root.Q<VisualElement>("DropButton");
            _pickButton = root.Q<VisualElement>("PickButton");
            _pickLabel  = _pickButton?.Q<Label>("PickLabel");
            _pickIcon   = _pickButton?.Q<VisualElement>("PickWeaponIcon");

            // Create Drop button programmatically — placed to the LEFT of weapon cells
            if (_dropButton == null && _weaponCellsContainer != null)
            {
                _dropButton = CreateDropButton();
                _dropIcon = _dropButton.Q<VisualElement>("DropWeaponIcon");
                // Insert into the same parent as WeaponCells (the Root container)
                var parent = _weaponCellsContainer.parent;
                if (parent != null)
                {
                    int wcIdx = parent.IndexOf(_weaponCellsContainer);
                    parent.Insert(wcIdx, _dropButton);
                }
                _dropButton.style.display = DisplayStyle.None;
            }

            // Create Pick button (weapon card) programmatically if missing
            if (_pickButton == null && _weaponCellsContainer != null)
            {
                _pickButton = CreatePickWeaponCard();
                _pickLabel  = _pickButton.Q<Label>("PickLabel");
                _pickIcon   = _pickButton.Q<VisualElement>("PickWeaponIcon");
                // Insert at index 0 (very top of the column, above drop)
                _weaponCellsContainer.Insert(0, _pickButton);
                _pickButton.style.display = DisplayStyle.None;
            }

            // Inject custom cooldown dial elements
            var wrap1 = root.Q<VisualElement>("Ability1Wrap");
            var cd1   = root.Q<VisualElement>("Ability1Cooldown");
            if (wrap1 != null && cd1 != null)
            {
                _dial1 = new CooldownDialElement();
                _dial1.style.position = Position.Absolute;
                _dial1.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
                _dial1.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
                _dial1.pickingMode = PickingMode.Ignore;
                _dial1.style.borderTopLeftRadius = _dial1.style.borderTopRightRadius =
                    _dial1.style.borderBottomLeftRadius = _dial1.style.borderBottomRightRadius = 44f;
                wrap1.Add(_dial1);
                wrap1.Remove(cd1);   // remove placeholder
            }

            var wrap2 = root.Q<VisualElement>("Ability2Wrap");
            var cd2   = root.Q<VisualElement>("Ability2Cooldown");
            if (wrap2 != null && cd2 != null)
            {
                _dial2 = new CooldownDialElement();
                _dial2.style.position = Position.Absolute;
                _dial2.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
                _dial2.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
                _dial2.pickingMode = PickingMode.Ignore;
                _dial2.style.borderTopLeftRadius = _dial2.style.borderTopRightRadius =
                    _dial2.style.borderBottomLeftRadius = _dial2.style.borderBottomRightRadius = 44f;
                wrap2.Add(_dial2);
                wrap2.Remove(cd2);
            }
        }

        // ── Joystick setup ─────────────────────────────────────────────────
        private void SetupJoystick(VisualElement root)
        {
            if (_joystickArea == null) return;
            _joystickArea.pickingMode = PickingMode.Position;
            _joystickArea.RegisterCallback<PointerDownEvent>(OnJoystickDown);
            _joystickArea.RegisterCallback<PointerMoveEvent>(OnJoystickMove);
            _joystickArea.RegisterCallback<PointerUpEvent>(OnJoystickUp);
            _joystickArea.RegisterCallback<PointerCancelEvent>(OnJoystickCancel);
        }

        private void OnJoystickDown(PointerDownEvent evt)
        {
            if (_joystickPointerId >= 0) return;   // already tracking one finger
            _joystickPointerId = evt.pointerId;
            _joystickArea.CapturePointer(evt.pointerId);

            // Convert panel-space position (top-left origin) to screen space for base ring
            Vector2 panelPos = evt.position;

            // Show base ring centred at touch
            float baseHalf = 65f;
            _joystickBase.style.left = panelPos.x - baseHalf;
            _joystickBase.style.top  = panelPos.y - baseHalf;
            _joystickBase.style.display = DisplayStyle.Flex;

            _joystickBaseCenter = panelPos;
            _joystickThumb.style.left = baseHalf - 28f;
            _joystickThumb.style.top  = baseHalf - 28f;

            evt.StopPropagation();
        }

        private void OnJoystickMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _joystickPointerId) return;

            Vector2 delta = (Vector2)evt.position - _joystickBaseCenter;
            float   mag   = delta.magnitude;
            Vector2 clamped = mag > JoystickMaxRadius ? delta / mag * JoystickMaxRadius : delta;

            // UIToolkit Y axis goes DOWN (0=top). Negate Y so finger-up = forward (+Y in game).
            MoveInput = new Vector2(clamped.x, -clamped.y) / JoystickMaxRadius;

            float baseHalf = 65f;
            _joystickThumb.style.left = baseHalf + clamped.x - 28f;
            _joystickThumb.style.top  = baseHalf + clamped.y - 28f;

            evt.StopPropagation();
        }

        private void OnJoystickUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _joystickPointerId) return;
            ResetJoystick(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnJoystickCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != _joystickPointerId) return;
            ResetJoystick(evt.pointerId);
        }

        private void ResetJoystick(int pointerId)
        {
            _joystickArea.ReleasePointer(pointerId);
            _joystickPointerId = -1;
            MoveInput = Vector2.zero;
            _joystickBase.style.display = DisplayStyle.None;
        }

        // ── Button setup ──────────────────────────────────────────────────
        private void SetupButtons()
        {
            RegisterFireButton(_fireButton);
            RegisterSimple(_reloadButton,  () => OnReload?.Invoke());
            RegisterSimple(_knifeButton,   () => OnKnifeSelect?.Invoke());
            RegisterSimple(_ability1Button, () => OnAbility1?.Invoke());
            RegisterSimple(_ability2Button, () => OnAbility2?.Invoke());
            RegisterSimple(_ultimateButton, () => OnUltimate?.Invoke());
            RegisterSimple(_jumpButton,     () => OnJump?.Invoke());
            RegisterSimple(_crouchButton,   () => OnCrouch?.Invoke());
            RegisterSimple(_standButton,    () => OnStand?.Invoke());
            RegisterSimple(_dropButton,     () => OnDropWeapon?.Invoke());
            RegisterSimple(_pickButton,     () => OnPickWeapon?.Invoke());

            // Weapon cells — tap to select
            if (_primaryCell != null)
            {
                _primaryCell.pickingMode = PickingMode.Position;
                _primaryCell.RegisterCallback<PointerDownEvent>(_ => OnPrimarySelect?.Invoke());
            }
            if (_secondaryCell != null)
            {
                _secondaryCell.pickingMode = PickingMode.Position;
                _secondaryCell.RegisterCallback<PointerDownEvent>(_ => OnSecondarySelect?.Invoke());
            }
        }

        /// Fire button is special: supports override (grenade) and tracks up event.
        private void RegisterFireButton(VisualElement el)
        {
            if (el == null) return;
            el.pickingMode = PickingMode.Position;

            el.RegisterCallback<PointerDownEvent>(evt =>
            {
                el.CapturePointer(evt.pointerId);
                if (_fireOverrideDown != null) _fireOverrideDown();
                else                           OnFireDown?.Invoke();
                evt.StopPropagation();
            });

            el.RegisterCallback<PointerUpEvent>(evt =>
            {
                el.ReleasePointer(evt.pointerId);
                if (_fireOverrideUp != null) _fireOverrideUp();
                else                         OnFireUp?.Invoke();
                evt.StopPropagation();
            });

            el.RegisterCallback<PointerCancelEvent>(evt =>
            {
                el.ReleasePointer(evt.pointerId);
                if (_fireOverrideUp != null) _fireOverrideUp();
                else                         OnFireUp?.Invoke();
            });
        }

        private static void RegisterSimple(VisualElement el, Action action)
        {
            if (el == null) return;
            el.pickingMode = PickingMode.Position;
            el.RegisterCallback<PointerDownEvent>(evt =>
            {
                action?.Invoke();
                evt.StopPropagation();
            });
        }

        // ── Sprite assignment ─────────────────────────────────────────────
        private void ApplySprites()
        {
            // Background image set directly on button element so it fills the full circle.
            SetIcon(_fireButton,   fireSprite);
            SetIcon(_reloadButton, reloadSprite);
            SetIcon(_knifeButton,  knifeSprite);
            SetIcon(_jumpButton,   jumpSprite);
            SetIcon(_crouchButton, crouchSprite);
            SetIcon(_standButton,  standSprite);
        }

        // ── Crouch UI toggle ──────────────────────────────────────────────
        /// <summary>
        /// Swaps crouch/stand button visibility. Called by SetCrouch() in PlayerController.
        /// </summary>
        public void SetCrouchMode(bool isCrouching)
        {
            if (_jumpButton   != null) _jumpButton.style.display   = isCrouching ? DisplayStyle.None : DisplayStyle.Flex;
            if (_crouchButton != null) _crouchButton.style.display = isCrouching ? DisplayStyle.None : DisplayStyle.Flex;
            if (_standButton  != null) _standButton.style.display  = isCrouching ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetIcon(VisualElement el, Sprite sprite)
        {
            if (el == null || sprite == null) return;
            el.style.backgroundImage = new StyleBackground(sprite);
        }

        // ── Fire override (grenade / ability hijack) ───────────────────────
        /// <summary>
        /// Replaces fire button callbacks while a grenade / special ability is active.
        /// Pass null for either action if not needed.
        /// </summary>
        public void SetFireOverride(Action onDown, Action onUp)
        {
            _fireOverrideDown = onDown;
            _fireOverrideUp   = onUp;

            // Dim the fire button to signal override mode
            if (_fireButton != null)
                _fireButton.style.opacity = 0.55f;
        }

        /// <summary>Removes grenade override and restores normal fire behaviour.</summary>
        public void ClearFireOverride()
        {
            _fireOverrideDown = null;
            _fireOverrideUp   = null;
            if (_fireButton != null)
                _fireButton.style.opacity = 1f;
        }

        // ── Weapon / ammo display API ──────────────────────────────────────
        public void SetPrimaryWeapon(Sprite icon, int maxAmmo)
        {
            SetIcon(_primaryIcon, icon);
            if (_primaryAmmoLabel != null) _primaryAmmoLabel.text = maxAmmo.ToString();
        }

        public void SetSecondaryWeapon(Sprite icon, int maxAmmo)
        {
            SetIcon(_secondaryIcon, icon);
            if (_secondaryAmmoLabel != null) _secondaryAmmoLabel.text = maxAmmo.ToString();
        }

        public void UpdatePrimaryAmmo(int current)
        {
            if (_primaryAmmoLabel != null) _primaryAmmoLabel.text = current.ToString();
        }

        public void UpdateSecondaryAmmo(int current)
        {
            if (_secondaryAmmoLabel != null) _secondaryAmmoLabel.text = current.ToString();
        }

        /// <summary>
        /// Highlights the active weapon cell and enlarges it.
        /// slot 1 = primary active, 0 = secondary active, -1 = knife (no highlight, equal sizes).
        /// </summary>
        public void SetActiveWeaponSlot(int slot)
        {
            Color goldBorder = new Color(1f, 0.78f, 0f, 0.85f);
            Color goldBg     = new Color(0.14f, 0.10f, 0f, 0.70f);
            Color greyBorder = new Color(0.65f, 0.65f, 0.65f, 0.28f);
            Color greyBg     = new Color(0f, 0f, 0f, 0.45f);

            ApplyCellStyle(_primaryCell,   _primaryAmmoLabel,   slot == 1, goldBorder, goldBg, greyBorder, greyBg);
            ApplyCellStyle(_secondaryCell, _secondaryAmmoLabel, slot == 0, goldBorder, goldBg, greyBorder, greyBg);
        }

        private static void ApplyCellStyle(VisualElement cell, Label lbl, bool active,
            Color goldBorder, Color goldBg, Color greyBorder, Color greyBg)
        {
            if (cell == null) return;
            // Same size for both — only border color changes
            cell.style.backgroundColor = new StyleColor(active ? goldBg   : greyBg);
            SetBorderColor(cell,          active ? goldBorder : greyBorder);
            cell.style.borderTopWidth = cell.style.borderBottomWidth =
                cell.style.borderLeftWidth = cell.style.borderRightWidth = active ? 2f : 1f;
            if (lbl != null)
            {
                lbl.style.color    = new StyleColor(active
                    ? new Color(1f, 0.86f, 0f)
                    : new Color(0.75f, 0.75f, 0.75f));
                lbl.style.fontSize = 14f;
            }
        }

        /// <summary>Backward-compat wrapper. Prefer <see cref="SetActiveWeaponSlot"/>.</summary>
        public void SetActiveWeapon(bool isPrimary) => SetActiveWeaponSlot(isPrimary ? 1 : 0);

        private static void SetBorderColor(VisualElement el, Color c)
        {
            el.style.borderTopColor    = new StyleColor(c);
            el.style.borderBottomColor = new StyleColor(c);
            el.style.borderLeftColor   = new StyleColor(c);
            el.style.borderRightColor  = new StyleColor(c);
        }

        // ── Ability display API ────────────────────────────────────────────
        public void SetAbility1Icon(Sprite icon) => SetIcon(_ability1Icon, icon);
        public void SetAbility2Icon(Sprite icon) => SetIcon(_ability2Icon, icon);

        /// <param name="t">0 = no cooldown, 1 = full cooldown overlay</param>
        public void SetAbility1Progress(float t, Color color)
        {
            if (_dial1 == null) return;
            _dial1.SetProgress(t, color);
        }

        public void SetAbility2Progress(float t, Color color)
        {
            if (_dial2 == null) return;
            _dial2.SetProgress(t, color);
        }

        public void SetAbility1Interactable(bool on)
        {
            if (_ability1Button == null) return;
            _ability1Button.pickingMode = on ? PickingMode.Position : PickingMode.Ignore;
            _ability1Button.style.opacity = on ? 1f : 0.45f;
        }

        public void SetAbility2Interactable(bool on)
        {
            if (_ability2Button == null) return;
            _ability2Button.pickingMode = on ? PickingMode.Position : PickingMode.Ignore;
            _ability2Button.style.opacity = on ? 1f : 0.45f;
        }

        // ── Ultimate display API ───────────────────────────────────────────

        public void SetUltimateIcon(Sprite icon) => SetIcon(_ultimateIcon, icon);

        /// <summary>
        /// Fills dots 1-N with orange, the rest stay dim grey.
        /// 0 = all dim, 5 = all lit orange.
        /// </summary>
        public void SetUltimateDots(int filledCount)
        {
            Color lit = new Color(1f, 0.75f, 0f, 1f);    // orange
            Color dim = new Color(0.3f, 0.3f, 0.3f, 0.8f); // grey
            for (int i = 0; i < 5; i++)
            {
                if (_ultimateDots[i] != null)
                    _ultimateDots[i].style.backgroundColor = new StyleColor(i < filledCount ? lit : dim);
            }
        }

        /// <summary>
        /// Turns all 5 dots green (ultimate charged / active).
        /// </summary>
        public void SetUltimateDotsActive(bool active)
        {
            Color green = new Color(0.1f, 1f, 0.2f, 1f);
            Color dim   = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            for (int i = 0; i < 5; i++)
            {
                if (_ultimateDots[i] != null)
                    _ultimateDots[i].style.backgroundColor = new StyleColor(active ? green : dim);
            }
        }

        public void SetUltimateInteractable(bool on)
        {
            if (_ultimateButton == null) return;
            _ultimateButton.pickingMode = on ? PickingMode.Position : PickingMode.Ignore;
            _ultimateButton.style.opacity = on ? 1f : 0.35f;
        }

        /// <summary>Show or hide the reload button (hidden when knife is equipped).</summary>
        public void SetReloadButtonVisible(bool visible)
        {
            if (_reloadButton == null) return;
            _reloadButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Hides every mobile-control element (fire, reload, knife, joystick,
        /// abilities, weapon cells, etc.) by setting inline display:none.
        /// Used by the post-match ceremony so only the scoreboard is visible.
        /// </summary>
        public void HideAllControls()
        {
            void Hide(VisualElement el) { if (el != null) el.style.display = DisplayStyle.None; }

            Hide(_fireButton);
            Hide(_reloadButton);
            Hide(_knifeButton);
            Hide(_joystickArea);
            Hide(_joystickBase);
            Hide(_ability1Button);
            Hide(_ability2Button);
            Hide(_ultimateButton);
            Hide(_jumpButton);
            Hide(_crouchButton);
            Hide(_standButton);
            Hide(_dropButton);
            Hide(_pickButton);
            Hide(_weaponCellsContainer);

            // Also hide the ability wrappers (contain cooldown dials)
            Hide(_ability1Button?.parent);
            Hide(_ability2Button?.parent);
            // Ultimate wrapper
            Hide(_ultimateButton?.parent);
        }

        // ── Drop / Pick API ───────────────────────────────────────────────

        /// <summary>Shows or hides the "Drop" button. When shown, updates the weapon icon.</summary>
        public void ShowDropButton(bool show, string weaponId = null)
        {
            if (_dropButton == null) return;
            _dropButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show && _dropIcon != null && !string.IsNullOrEmpty(weaponId))
            {
                var tex = Resources.Load<Texture2D>(GetWeaponWhiteIconPath(weaponId));
                if (tex != null)
                    _dropIcon.style.backgroundImage = new StyleBackground(tex);
            }
        }

        private static string GetWeaponWhiteIconPath(string weaponId)
        {
            switch (weaponId)
            {
                case "talon_ar":      return "Icons/Talon-ARWhiteIcon";
                case "bolt":          return "Icons/BoltWhiteIcon";
                case "knife":         return "Icons/WhiteIconDefaultKnife";
                case "default_knife": return "Icons/WhiteIconDefaultKnife";
                default:              return "Icons/Talon-ARWhiteIcon";
            }
        }

        /// <summary>Shows or hides the "Pick" button with weapon icon and name.</summary>
        public void ShowPickButton(bool show, string weaponId)
        {
            if (_pickButton == null) return;
            _pickButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show && !string.IsNullOrEmpty(weaponId))
            {
                // Update label
                string displayName = GetWeaponDisplayName(weaponId);
                if (_pickLabel != null)
                    _pickLabel.text = displayName;

                // Update icon — try skin icon first, then default weapon icon
                if (_pickIcon != null)
                {
                    Texture2D tex = LoadWeaponColorIcon(weaponId);
                    if (tex != null)
                        _pickIcon.style.backgroundImage = new StyleBackground(tex);
                    else
                        _pickIcon.style.backgroundImage = StyleKeyword.None;
                }
            }
        }

        /// <summary>Loads the color icon for a weapon config ID (may be a skin ID like "talon_skull").</summary>
        private static Texture2D LoadWeaponColorIcon(string weaponConfigId)
        {
            if (string.IsNullOrEmpty(weaponConfigId)) return null;

            // 1) Try matching as a skinId across all weapon skins
            foreach (var baseWeaponId in new[] { "talon_ar", "bolt" })
            {
                var skin = ArtisansGuns.Data.WeaponSkinDefinition.GetSkin(baseWeaponId, weaponConfigId);
                if (skin != null)
                {
                    var tex = Resources.Load<Texture2D>(skin.iconPath);
                    if (tex != null) return tex;
                }
            }

            // 2) Try as a base weapon ID via default skin
            var defSkin = ArtisansGuns.Data.WeaponSkinDefinition.GetDefaultSkin(weaponConfigId);
            if (defSkin != null)
            {
                var tex = Resources.Load<Texture2D>(defSkin.iconPath);
                if (tex != null) return tex;
            }

            // 3) Fallback to WeaponDefinition icon
            var weapon = ArtisansGuns.Data.WeaponDefinition.GetWeaponById(weaponConfigId);
            if (weapon != null)
            {
                var tex = Resources.Load<Texture2D>(weapon.iconPath);
                if (tex != null) return tex;
            }

            return null;
        }

        private static string GetWeaponDisplayName(string weaponId)
        {
            return weaponId switch
            {
                "talon_ar" => "TALON-AR",
                "bolt" => "BOLT",
                _ => weaponId.Replace('_', ' ').ToUpper()
            };
        }

        /// <summary>Clears a weapon cell (icon + ammo) to empty, e.g. after dropping.</summary>
        public void ClearWeaponCell(bool isPrimary)
        {
            if (isPrimary)
            {
                if (_primaryIcon != null)
                    _primaryIcon.style.backgroundImage = StyleKeyword.None;
                if (_primaryAmmoLabel != null)
                    _primaryAmmoLabel.text = "";
            }
            else
            {
                if (_secondaryIcon != null)
                    _secondaryIcon.style.backgroundImage = StyleKeyword.None;
                if (_secondaryAmmoLabel != null)
                    _secondaryAmmoLabel.text = "";
            }
        }

        /// <summary>Creates a small HUD text button programmatically.</summary>
        private static VisualElement CreateHudTextButton(string name, string text)
        {
            var btn = new VisualElement();
            btn.name = name;
            btn.style.width           = 80f;
            btn.style.height          = 36f;
            btn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 0.75f));
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 8f;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 1f;
            var borderColor = new StyleColor(new Color(0.8f, 0.8f, 0.8f, 0.5f));
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = borderColor;
            btn.style.justifyContent = Justify.Center;
            btn.style.alignItems     = Align.Center;
            btn.style.marginTop      = 4f;
            btn.pickingMode = PickingMode.Position;

            var lbl = new Label(text);
            lbl.name = "PickLabel";
            lbl.style.color    = new StyleColor(Color.white);
            lbl.style.fontSize = 13f;
            lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            lbl.pickingMode = PickingMode.Ignore;
            btn.Add(lbl);

            return btn;
        }

        /// <summary>Creates a compact Drop button styled to match the weapon area.</summary>
        private static VisualElement CreateDropButton()
        {
            var btn = new VisualElement();
            btn.name = "DropButton";
            // Position to the left of weapon cells
            btn.style.position = Position.Absolute;
            btn.style.right    = 260f; // further left of weapon cells
            btn.style.bottom   = 310f; // same bottom as WeaponCells
            btn.style.width    = 96f;
            btn.style.height   = 96f;
            btn.style.backgroundColor = new StyleColor(new Color(0.35f, 0.05f, 0.05f, 0.75f));
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 8f;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 2f;
            var bc = new StyleColor(new Color(1f, 0.25f, 0.25f, 0.7f));
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = bc;
            btn.style.justifyContent = Justify.Center;
            btn.style.alignItems     = Align.Center;
            btn.pickingMode = PickingMode.Position;

            // Weapon white icon (centered, fills most of the button)
            var icon = new VisualElement();
            icon.name = "DropWeaponIcon";
            icon.style.width  = 70f;
            icon.style.height = 70f;
            icon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            icon.pickingMode = PickingMode.Ignore;
            btn.Add(icon);

            // Red X overlay — diagonal line visual via two thin rotated bars
            var xBar1 = new VisualElement();
            xBar1.style.position = Position.Absolute;
            xBar1.style.width  = 80f;
            xBar1.style.height = 4f;
            xBar1.style.backgroundColor = new StyleColor(new Color(1f, 0.2f, 0.2f, 0.9f));
            xBar1.style.top = 46f;
            xBar1.style.left = 8f;
            xBar1.style.rotate = new Rotate(Angle.Degrees(45f));
            xBar1.pickingMode = PickingMode.Ignore;
            btn.Add(xBar1);

            var xBar2 = new VisualElement();
            xBar2.style.position = Position.Absolute;
            xBar2.style.width  = 80f;
            xBar2.style.height = 4f;
            xBar2.style.backgroundColor = new StyleColor(new Color(1f, 0.2f, 0.2f, 0.9f));
            xBar2.style.top = 46f;
            xBar2.style.left = 8f;
            xBar2.style.rotate = new Rotate(Angle.Degrees(-45f));
            xBar2.pickingMode = PickingMode.Ignore;
            btn.Add(xBar2);

            return btn;
        }

        /// <summary>
        /// Creates the Pick weapon card inspired by lobby WeaponsTab cells:
        /// dark background, orange top accent, weapon name header section,
        /// large icon area below.
        /// </summary>
        private static VisualElement CreatePickWeaponCard()
        {
            var card = new VisualElement();
            card.name = "PickButton";
            card.style.width           = 160f;
            card.style.backgroundColor = new StyleColor(new Color(0.04f, 0.04f, 0.06f, 0.82f));
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 8f;
            // Subtle border
            card.style.borderTopWidth    = 2f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth   = 1f;
            card.style.borderRightWidth  = 1f;
            // Orange top accent (like lobby cells)
            card.style.borderTopColor    = new StyleColor(new Color(1f, 0.42f, 0.24f, 0.5f));
            var sideBorder = new StyleColor(new Color(1f, 1f, 1f, 0.1f));
            card.style.borderBottomColor = sideBorder;
            card.style.borderLeftColor   = sideBorder;
            card.style.borderRightColor  = sideBorder;
            card.style.flexDirection  = FlexDirection.Column;
            card.style.alignItems    = Align.Center;
            card.style.alignSelf     = Align.FlexEnd;
            card.style.marginBottom  = 6f;
            card.style.overflow      = Overflow.Hidden;
            card.pickingMode = PickingMode.Position;

            // ── Top section: "PICK" label centered ──
            var header = new VisualElement();
            header.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            header.style.backgroundColor = new StyleColor(new Color(1f, 0.42f, 0.24f, 0.10f));
            header.style.paddingTop    = 5f;
            header.style.paddingBottom = 5f;
            header.style.justifyContent = Justify.Center;
            header.style.alignItems    = Align.Center;
            header.pickingMode = PickingMode.Ignore;

            var pickTag = new Label("PICK");
            pickTag.name = "PickLabel";
            pickTag.style.color    = new StyleColor(new Color(1f, 0.55f, 0.3f, 0.7f));
            pickTag.style.fontSize = 11f;
            pickTag.style.unityFontStyleAndWeight = FontStyle.Bold;
            pickTag.style.letterSpacing = 2f;
            pickTag.style.unityTextAlign = TextAnchor.MiddleCenter;
            pickTag.pickingMode = PickingMode.Ignore;
            header.Add(pickTag);

            card.Add(header);

            // ── Icon area (fills the rest, like lobby cells) ──
            var icon = new VisualElement();
            icon.name = "PickWeaponIcon";
            icon.style.width  = new StyleLength(new Length(100, LengthUnit.Percent));
            icon.style.height = 90f;
            icon.style.paddingLeft   = 10f;
            icon.style.paddingRight  = 10f;
            icon.style.paddingTop    = 6f;
            icon.style.paddingBottom = 8f;
            icon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            icon.pickingMode = PickingMode.Ignore;
            card.Add(icon);

            return card;
        }

        // ═══════════════════════════════════════════════════════════════════
        // CooldownDialElement — arc-stroke ring drawn with Painter2D
        // progress=1 → full ring (ability ready/active)
        // progress<1 → partial ring filling clockwise from top (recharging)
        // Caller passes Color.green for active, orange for recharging.
        // ═══════════════════════════════════════════════════════════════════
        private class CooldownDialElement : VisualElement
        {
            private float _progress;   // 0-1
            private Color _color = new Color(1f, 0.55f, 0f, 0.9f); // default orange

            public CooldownDialElement()
            {
                generateVisualContent += Draw;
            }

            public void SetProgress(float t, Color color)
            {
                _progress = Mathf.Clamp01(t);
                _color    = color;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var painter = ctx.painter2D;
                float cx = contentRect.width  * 0.5f;
                float cy = contentRect.height * 0.5f;
                float r  = Mathf.Min(cx, cy) - 3f;  // ring sits 3px inside wrap edge

                // Always draw a dim background full ring
                painter.strokeColor = new Color(0.15f, 0.15f, 0.15f, 0.45f);
                painter.lineWidth   = 5f;
                painter.BeginPath();
                painter.Arc(new Vector2(cx, cy), r, -90f, 270f);
                painter.Stroke();

                if (_progress <= 0f) return;

                // Draw the progress arc clockwise from top
                float endDeg = -90f + 360f * _progress;
                painter.strokeColor = new Color(_color.r, _color.g, _color.b, 0.92f);
                painter.lineWidth   = 5f;
                painter.BeginPath();
                painter.Arc(new Vector2(cx, cy), r, -90f, endDeg);
                painter.Stroke();
            }
        }
    }
}
