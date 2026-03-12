KNIFE ICONS DIRECTORY
=====================

This directory contains icons for knife skins in the game.

REQUIRED ASSET:
- DefaultKnife.png (256x256 pixels)

FORMAT REQUIREMENTS:
- PNG format with transparency
- 256x256 pixels recommended
- White silhouette or colored design
- Transparent background

USAGE:
The default knife icon is referenced in KnifeSkinDefinition.cs as:
iconPath: "Icons/Knives/DefaultKnife"

Unity will automatically load this from the Resources folder.

TO ADD NEW KNIFE SKINS:
1. Add the icon image to this folder (e.g., DragonKnife.png)
2. Update KnifeSkinDefinition.cs with:
   new KnifeSkin(
       "dragon",
       "DRAGON BLADE",
       "Icons/Knives/DragonKnife",
       defaultSkin: false,
       skinCost: 500
   )
