GWENT CARD EDITOR
User Guide

Beta localization: the complete interface is available in English, Russian and
Simplified Chinese. Other interface languages are partial; untranslated items
fall back to English. Card category names use the selected client's game files.
The Card Text section opens on English by default.

1. Start and open a game

Run GwentCardEditor.exe. The first-time interface language is English. Choose
Open GWENT Client and select the GWENT installation folder. The editor reads
the selected client; editing and Apply do not install changes into the game.

The left side lists cards. Use the faction tabs, category selector and search
box to find a card. Search accepts a card name or card ID. Click a column title
to sort; click it again to reverse the order. Cards, tokens and leaders are
shown in their own categories. Select a row to edit it.

2. Card sections

Parameters
Edit only fields that are enabled for the selected card:
- Power: current card power.
- Armor: card armor, when supported.
- Provision: deck-building cost. For leaders this is the leader's deck bonus.
- Rarity, main faction, optional secondary faction and categories: choose from
  the available values. The two factions must be different.
  Changing a faction does not rewrite the card's ability conditions or targets.
- Initial statuses: select the status checkboxes offered for that card. Their
  localized names are added to the card description. Statuses that are not
  supported for that card remain unavailable.
- Other verified switches: use the named checkbox or numeric field shown for
  the card. Disabled controls cannot be changed.

Ability
This section contains verified numeric values in the existing ability, such
as damage, boost, target count, charges, timers, fees or other values shown for
the selected card. The label and nearby note explain what the field controls.
Change only the value you intend to change. Where a number also appears in card
text, the editor updates the supported description entries together with the
ability value. Review the confirmation list before applying.
Ability changes made with this editor only work in Training mode against the
computer. They do not work in multiplayer matches.

Texts
Each tab represents a language available in the selected client. Edit the
card's name, ability description or flavor text in the corresponding field.
Unavailable source entries are not created automatically. Preserve formatting
tags and variable markers. Use the visible line-break notation rather than
pressing Enter inside a single-line game text.

Card Art
Choose an image file, position and crop it in the preview, then export an art
package. Installing and restoring an art package are separate actions. Standard
art replacement does not replace the premium animation.

Card Back
Choose a replacement image for a card back. Export the package, then install
it only after the client is closed. A backup package is created for restoration.

Global Appearance
Preview and choose supported card frames or overlay options. These settings
apply to the supported card groups, not to one selected card. Animated frames
and starter-deck filtering require the optional BepInEx setup described by the
controls. Static appearance options do not require that plugin.

Audio
Use another card's native audio where the editor enables this option, or
prepare a custom premium-preview clip. Custom audio playback requires its
optional plugin. Exporting a package does not install it. Close GWENT before
installation or restoration, and follow the confirmation shown by the editor.

3. Balance Council

Balance Council is enabled by default. The editor uses a locally cached snapshot
from the official playgwent.com deck builder. Click Update Balance to download
the latest snapshot, and update it again after each new Council change; snapshots
are not refreshed automatically. With the option enabled, shown power and
provision values include the Council adjustment, and export handles that
adjustment for the selected client. Turn it off to use only local card data, for
example when the client does not include Council changes. Do not change the
source midway through a session.

4. Apply, undo and export

Apply records the selected card's changes in the current editor session; it does not
write to the game installation. Use Undo to reverse the last applied edit.
Reset clears pending edits for the current session. Review the change list before
confirming an edit or export.

Export creates a new output folder and never overwrites the selected source.
Choose a folder that does not already exist. For a client export, install only
the exported data_definitions archive into a separate GWENT test installation;
keep the accompanying editor backup package. Close the game before replacing
files. To undo a package, restore packages in reverse order of installation.

5. Safe use

- Keep the original client unchanged and test exports in a separate GWENT
  installation.
- Keep every backup package until you no longer need to undo its changes.
- Do not edit a card or setting whose control is disabled.
- If the editor says source files changed outside the session, stop and reopen
  the client rather than overwriting those changes.
- Back up your test installation before installing exported game data or media.
- The editor checks supported data structures, but cannot guarantee game
  balance or every interaction. Verify changes in game.
