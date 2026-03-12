# QUICK PLACEHOLDER ICON GUIDE

## Create a Temporary Knife Icon in 2 Minutes

While you model the 3D knife, you can test the system with a placeholder icon.

### Option 1: Duplicate Existing Icon (FASTEST - 30 seconds)

1. In Unity Project window, navigate to:
   `Assets/Resources/Icons/`

2. Find `BoltWhiteIcon.png` or `Talon-ARWhiteIcon.png`

3. **Right-click** → **Duplicate** (Ctrl+D)

4. Rename to `DefaultKnife`

5. **Drag and drop** into `Assets/Resources/Icons/Knives/` folder

6. Done! The system will now load this icon.

---

### Option 2: Create Simple Knife Silhouette (5 minutes)

**Using External Tool**:
1. Open any image editor (Photoshop, GIMP, Paint.NET, etc.)
2. Create 256x256 canvas with transparent background
3. Draw simple knife shape in white:
   ```
   Handle: 60x80 rectangle at bottom
   Blade: 40x140 triangle pointing up
   Edge: Add diagonal cut on blade side
   ```
4. Export as PNG with transparency
5. Save as `DefaultKnife.png`
6. Import to Unity: `Assets/Resources/Icons/Knives/`

**Unity Import Settings**:
- Select the image in Project window
- In Inspector:
  - Texture Type: **Sprite (2D and UI)**
  - Sprite Mode: **Single**
  - Pixels Per Unit: 100
  - Filter Mode: Bilinear
  - Max Size: 256
  - Compression: None
- Click **Apply**

---

### Option 3: Use Text as Placeholder (1 minute)

**Create text-based icon**:
1. Use online tool: [placeholder.com](https://placeholder.com)
2. Generate 256x256 image with text "KNIFE"
3. Download as PNG
4. Rename to `DefaultKnife.png`
5. Import to Unity: `Assets/Resources/Icons/Knives/`

---

### Option 4: Screenshot from 3D Modeling Software (2 minutes)

While modeling the 3D knife:
1. Position camera for good angle
2. Set white material on knife
3. Dark/transparent background
4. Take screenshot
5. Crop to 256x256 in any editor
6. Save as `DefaultKnife.png`
7. Import to Unity

---

## Testing the Icon

After adding the icon:

1. **Restart Unity** (if needed to refresh Resources)
2. Run game in Editor
3. Login
4. Navigate to Weapons tab
5. **Knife slot should now show the icon**

If the icon doesn't appear:
- Check Unity Console for "Texture not found" errors
- Verify file is at exact path: `Assets/Resources/Icons/Knives/DefaultKnife.png`
- Right-click icon → **Reimport**
- Check Texture Type is "Sprite (2D and UI)"

---

## Icon Specifications (For Final Asset)

When creating the final knife icon:

### Visual Style:
- Match existing weapon icons (Talon-AR, Bolt style)
- White silhouette or colored design
- High contrast for visibility
- Side view (profile) usually works best

### Technical:
- **Format**: PNG with alpha channel
- **Dimensions**: 256x256 pixels (or 512x512 for HD)
- **Color Mode**: RGBA (with transparency)
- **Background**: Fully transparent

### Composition:
- Knife should occupy ~80% of canvas height
- Center positioned
- Blade pointing up or diagonal
- Add subtle glow/outline if desired (matching weapon icons)

### Examples of Good Knife Icons:
- CS:GO knife icons (simple, recognizable)
- Valorant melee icons (stylized, high contrast)
- Call of Duty knife icons (detailed but clear silhouette)

---

## Replacing the Placeholder

When your 3D model is ready:

1. Render/screenshot the knife from good angle
2. Process in image editor:
   - Crop to square (1:1 aspect ratio)
   - Remove background (make transparent)
   - Apply white/colored styling to match weapons
   - Resize to 256x256 or 512x512
3. Export as PNG with transparency
4. **Replace** `DefaultKnife.png` in Unity
5. Unity will automatically update all references

No code changes needed - just replace the file!

---

## Quick Reference: Existing Icon Locations

Check these for style reference:
```
Assets/Resources/Icons/
├── BoltIcon.png          (Pistol - colored)
├── BoltWhiteIcon.png     (Pistol - white silhouette)
├── CrimsonIcon.png       (Character icon)
├── Talon-ARIcon.png      (Rifle - colored)
└── Talon-ARWhiteIcon.png (Rifle - white silhouette)
```

**Recommended style**: White silhouette (like BoltWhiteIcon.png)
- Clean, consistent with UI theme
- High contrast on dark backgrounds
- Easy to see at any size

---

Generated: Feb 8, 2026
For: Knife System v1.0
