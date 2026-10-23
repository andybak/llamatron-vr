# Original presentation and game flow

1. The player uses the decoded `F.CMP` bitmap font for menus and the world-space
   HUD. CS:54f6 reads character pointers at font offset 2, advances by glyph word
   2, and CS:559f draws word 4 plus one rows from offset 6. The final shared
   `]`/`^`/`_` glyph is truncated in the original resource; the reconstruction
   displays a complete question-mark fallback instead of reading adjacent memory.
2. `tools/export_presentation_assets.py` exports the font, `SCORES.ORI`, and the
   original scroll layouts from the decoded image. Story starts at DS:14e1,
   credits at DS:117a, and the illustrated field guide at DS:1bc8. CS:5363-54f6
   supplies wrapping, 21-pixel line steps, centered null-terminated headings,
   low-four-bit colors, signed spacing adjustments and inline sprites. Sprite
   commands index the main bank pointer table at entry 14 plus a big-endian word.
   Plain-text sidecars are retained alongside the colored, illustrated layouts.
   P, the page's Pause/Resume button, or controller B/Y holds and resumes the
   scroll. The mouse wheel can move through a paused page without resuming it.
3. The title offers solo, droid or simultaneous two-player games, alternating
   turns and independent aim. Menus suspend the current simulation; M, Escape
   or the toolbar toggles the title menu and resumes its game with its pause state
   intact. A/X uses the same menu action; trigger/Space accepts continue prompts.
   New games reset mutable wave scripts. Loading a
   portable state or choosing a test wave opens gameplay. The browser toolbar
   keeps saving/exporting available while a player game is suspended in a menu,
   enables pause while playing, and keeps loading/testing available at the title.
4. The original CS:0269 attract dispatch is story, demo, credits, demo. Demos use
   the recovered three-wave scripted control and the original initial-wave range;
   they neither overwrite the manual save slot nor submit high scores. Idle delay
   and readable scroll speed are presentation choices, not verified DOS timings.
5. CS:5da2 uses ten records of eight decimal score characters and eight name
   characters. A new score must exceed an existing record; equal scores follow
   existing entries. Game over shows the original G3 graphic and sample 30, then
   offers qualifying names for each alternating player. Scores persist separately
   from portable games in `LlamatronHighScores.v1`. Browser storage remains local
   to the site/profile; this is not a shared online leaderboard.
   Typing replaces the remembered name; Enter accepts it without editing. The
   post-game table returns to title after twelve seconds, then resumes the idle
   attract cycle. A manually opened score table stays open until dismissed.
6. Pixel panels render inside the scene with the existing stereo-capable sprite
   shader. Keyboard, mouse and stick/trigger navigation share the menu commands.
   This does not resolve the deferred Quest Browser immersive rendering defect.

## Validation

1. `dotnet run --project standalone/Checks/Llamatron.Checks.csproj` covers ranking,
   score ties, eight-character names, eviction, binary persistence and corrupt
   score data alongside the existing gameplay/save checks.
2. A native player accepts `--desktop --presentation-check` to exercise mode
   selection, suspended clocks, resume, paused saves, test-wave and load entry,
   the attract dispatch, and game-over/name-entry/high-score transitions. This
   run uses an in-memory score table and does not write the user's high scores.
   It also waits for the real twelve-second score-table return and twenty-second
   idle-title transition, rather than substituting calls to the transition methods.
   The scroll must hold its exact offset while paused and advance after resuming.
3. `--desktop --presentation-page Title --presentation-capture <absolute.png>`
   captures a page through the real game camera. Other pages include Scores,
   NameEntry, Instructions, Story, Guide, Credits, GameOver and Demo. Scroll-page
   previews begin partway through their content to expose text and illustrations.
4. Desktop renders of title, high scores, name entry, controls, illustrated story,
   field guide, credits, game over and running demo have been inspected. Native
   timed flow checks passed. Web release `8b07655` deployed successfully, with
   matching HTML/offline versions. Manual browser input/persistence checks remain
   necessary before claiming the complete browser flow is verified.
