GWENT CARD EDITOR
User Guide

The complete interface is available in all 13 supported languages: English,
Russian, Simplified and Traditional Chinese, German, Spanish (Spain and Mexico),
French, Italian, Japanese, Korean, Polish and Brazilian Portuguese. Several
translations are machine generated and may contain errors. Card category names
use the selected client's game files.
The Card Text section opens on English by default.

1. Start and open a game

Run GwentCardEditor.exe. The first-time interface language is English. If a
standard path is found, the editor opens the official GOG client automatically.
Use Open Official GOG Client to return to it, or Open GWENT Client to select a
different installation. The last selected source is remembered. The source
line shows its path and a short archive SHA-256. The editor reads the selected
client; editing and Apply do not install changes into the game.
The first time a client is opened, the editor saves its original `data_definitions`
to `%LOCALAPPDATA%\GwentCardEditor\DefaultDataDefinitions` and never replaces it
automatically. Use Open Default Data Definitions in the toolbar to locate the copy.

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
  supported for that card remain unavailable; the reason is shown beside the
  controls and in a tooltip. Initial Status Coverage opens counts by status and
  a list of blocker reasons, and can save the report as Markdown.
- Other verified switches: use the named checkbox or numeric field shown for
  the card. Disabled controls cannot be changed.

Ability
This section contains verified numeric values in the existing ability, such
as damage, boost, target count, charges, timers, fees or other values shown for
the selected card. The label and nearby note explain what the field controls.
Change only the value you intend to change. Where a number also appears in card
text, the editor updates the supported description entries together with the
ability value. Review the confirmation list before applying.
Ability changes made with this editor only work in Training mode against AI
opponents. They do not work in multiplayer matches.

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
Undo All Visual Changes checks the saved art, card-back and appearance packages,
then restores them in reverse order. If a checksum differs, no files are changed.

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

Shared draft
After editing a card, select another one and continue. Changes are collected in
one project draft automatically. Apply Changes opens a list grouped by card.
Select a card to continue editing it, or remove all its changes. Undo and Redo
work with the shared draft.

4. Apply, undo and export

Apply Changes accepts the reviewed edits into the editor project; it does not
write to the game installation. Undo reverses the last operation, Redo restores
it, and Reset clears the current session. Export creates a package with the changes.

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
