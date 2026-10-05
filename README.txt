TinyRec 9  -  your own macro recorder (C# / .NET 8)
====================================================

OPEN IT
  dist\TinyRec9.exe      <- double-click this. It's the whole app in one file.
  Tip: right-click it > "Pin to Start" or "Send to > Desktop (create shortcut)".

  Your recordings, images, drawings and settings live in  dist\data\
  (next to the .exe). Move the exe and the data folder together.

HOTKEYS (work in any program; change them in Settings)
  F8   start / stop recording          F9   play / stop
  F6   auto clicker on / off           F7   draw mode on / off
  F12  PANIC - stops everything        F10  full window / mini bar
  F11  pause / continue (recording or playback picks up where it left off)
  Ctrl+Alt+Up / Down / 0   playback speed faster / slower / normal
  Esc  finishes draw mode

PAGES
  Record & Play  record, play, loop once / N times / forever, pause between
                 loops, live speed (slider or Ctrl+Alt+Up/Down while playing)
  Sequences      chain recordings together. Steps run top to bottom:
                   Wait for image  - holds everything until that picture is on
                                     screen, then clicks it, presses a key, does
                                     both, or just carries on (no time limit
                                     unless you set one). "Or carry on if" lets a
                                     second picture end the wait too.
                   Play recording  - plays one from your Library
                   Pause           - waits a number of seconds
                   Repeat          - runs the steps above it again, N times
                 Run it once, N times or forever. A switch makes F9 and the
                 mini bar run the sequence instead of the single recording.
                 The play button on a step starts the run from that step.
                 A Play step can also watch for an image while it plays.
                 The Run log shows what happened; click a line to jump to its step.
  Library        every saved recording - search, rename, duplicate,
                 export as .txt or .zip bundle (recording + its images), import.
                 Star a recording to keep it at the top; "Favorites" shows only those.
  Editor         timeline at the top (dots = clicks, bars = held keys and drags,
                 flags = image steps; scroll to zoom, drag to move, click a mark
                 to select it). Table, plain-text or Run log view; add / edit /
                 delete / nudge timing, shift, scale, thin mouse moves, "wait
                 before this action" to set one gap exactly, insert image steps
                 (click / press a key / both / only wait). Ctrl+Z / Ctrl+Y undo, redo.
  Auto Clicker   interval, random jitter, single / double / hold, follow cursor,
                 one fixed spot or several spots. "Record spots" lets you click
                 the spots, then Done (or F6) - it can start clicking right away.
  Images         snip part of the screen, test if TinyRec can find it
  Draw           boxes, circles, arrows, pen, markers, text on a see-through
                 layer (clicks go straight through it)
  Settings       theme (Neon / Dark / Light / System / Custom), accent colour (six presets
                 or any colour you pick), animations, the glowing window border,
                 size, hotkeys, window behaviour, where image steps look (whole
                 screen or one window, e.g. Roblox), run log

REBUILDING FROM THE CODE (only if you change the code)
  build.bat        builds dist\TinyRec9.exe (installs the .NET 8 SDK into your
                   user folder the first time - no admin needed)
  dev.bat          closes TinyRec, rebuilds, and starts it again
  test_engine.bat  runs the 196 engine checks (timing, file format, etc.)

  App\   the Windows app (WPF)          Core\  recording format, player, files
  Core.Tests\  the engine checks

Recordings are plain text - open any .txt in Notepad to read or edit it:
  # TinyRec v1
  0.000 MOVE 500 400
  0.120 LEFT_DOWN 500 400
  0.180 LEFT_UP 500 400
  0.500 KEY_DOWN a
  1.000 IMAGE_CLICK ok_button.png conf=0.90 timeout=10      (timeout=0 = wait forever)

  Image step options: conf=0.90  timeout=10 (0 = no limit)  button=left|right|double
                      key=e  onfail=skip|stop|wait  or=other.png,another.png

Sequences are saved as small readable files in dist\data\sequences\*.json.
Run logs are plain text in dist\data\logs\ (the newest 60 are kept).

NEW IN 9 (keyboard only / mouse only, and auto clicking with a recording)
  - keyboard, mouse or both. Record & Play has two choices:
      "Record"     what the NEXT recording captures: mouse + keyboard,
                   keyboard only, or mouse only
      "Play back"  what playback sends from ANY recording, old ones included:
                   everything, keyboard only, or mouse only. A recording keeps
                   its full length either way.
    A recording step in a sequence has its own "Play back" choice.
  - auto click alongside. Three ways, pick what fits:
      * Record & Play > "Run the auto clicker while it plays": the Auto Clicker
        page's setup starts with playback and stops when playback ends
      * the cog on the Sequences page > "Run the auto clicker for as long as
        this sequence runs": same, for a whole sequence
      * a recording step > "Auto click...": pick spots on screen and a pace;
        TinyRec clicks them only while that one recording plays
    Pausing (F11) pauses the clicks too. Best with "Keyboard only", so the
    recording's own mouse moves don't fight the clicks.
  - "Click" step in a sequence: clicks one or more spots N times (or for N
    seconds), then carries on. No spots = clicks wherever the pointer is.
  - Editor > "Clicks": adds a run of clicks on a spot you pick to a recording
    (e.g. 20 clicks, 10 a second). They are ordinary clicks afterwards.

  MORE IN 9 - the "tons of settings" round. Everything below is optional; a
  setting left alone behaves exactly as before.
  - a finer filter. "Custom" next to Keyboard only / Mouse only lets you tick
    pointer moves, clicks, scrolling, keys and image steps one by one, and
    name keys to leave out (tab, esc, ctrl ...). Works for recording, for
    playback, and per recording step in a sequence.
  - "More click settings" (Click step, clicks alongside, Auto Clicker page):
      order        in turn / random / there and back, N clicks per spot
      timing       random variation, wait before the first click, bursts
                   (N clicks, then rest N seconds)
      the click    hold each press, land up to N px off the spot, careful
                   (settle first - games) or fast, put the pointer back after
      keys         press a key with each click, or INSTEAD of clicking
      pictures     click wherever a picture is; only click while a picture
                   is (or is not) on screen; stop for good when one shows
      limits       stop after N clicks or N seconds
  - new kinds of sequence step:
      Keys             press a key or combination N times, or type text
      Jump             go to another step - always, or only if a picture is /
                       isn't on screen; with a limit on how often
      End here         end this full run early, or the whole run - same
                       conditions
      Bring a window to the front
      Alert            a message (and a sound) when the run gets there
  - every step: "When it runs" - every full run, only the first, every one
    but the first, every Nth; and a chance in % that it happens at all.
  - image step: can wait until its picture has GONE (loading screens).
  - recording step: play only part of it (from / to seconds), keep replaying
    it for N seconds, vary its speed a little at random each time.
  - the whole sequence (cog): wait before the first step, random extra gap
    between steps, random extra pause between full runs, a time limit, what
    to do when a step fails (stop / start again from step 1 / skip it),
    bring a window to the front first, a sound when it ends, and
    "Always watch": pictures looked for the whole time, each with its own
    action - click it, press a key, start again from step 1, stop, or alert.
  - Record & Play: random extra pause between loops, a time limit for
    playback, a sound when playback ends, stop recording by itself after N
    seconds.
  - Auto Clicker page: "There and back" order, stop after N seconds, and a
    "More" card with the click settings above.
  Paused time never counts toward a time limit.

NEW IN 8 (sequences get deeper - everything from 7 is unchanged underneath)
  - spacing: every step has its own "wait before" and "wait after" (the pencil
    on a step), and the cog on the Sequences page sets one gap between all steps
  - settings saved with each sequence (the cog, or "Settings for this sequence"):
    a note, the gap between steps, how many times it runs, the pause between
    full runs, its own recording speed, which window image steps look in, and
    what the TinyRec window does while it runs
  - more per step: your own name for it, an on/off switch (the tick on the row -
    off = skipped), a click offset for image steps, its own speed for a
    recording, and a pause that is a different length every time
  - "Keep this window open while it runs": the step that is running lights up in
    the list, scrolls into view and says what it is doing; every step shows how
    many times it has run
  - watch while it plays: a Play step can look for an image the whole time its
    recording plays, and only moves on once that image has been seen. Choose
    whether it cuts the recording short or lets it finish, whether the recording
    replays until then, whether to click the image / press a key, and a time
    limit
  - a recording that presses Space or Enter can no longer press TinyRec's own
    buttons (it used to hit Stop when the TinyRec window had the focus);
    TinyRec's windows ignore keys while something plays - hotkeys still work


NEW IN 7 (the design update - everything from 6 is unchanged underneath)
  - Neon theme: deep navy, a purple-to-blue sweep on buttons and sliders, pink
    for Record, a soft glow around cards and the selected page
  - any accent colour: click the rainbow dot in Settings > Appearance and pick
    from the grid or type a hex code like #7c4dff
  - glowing border around the window in colours you choose (up to 4): one colour
    breathes, several fade into each other; slow / medium / fast; always or only
    while recording / playing; pink while recording, accent while playing
  - animations: pulsing ring on Record while recording, buttons light up on
    hover, pages fade in, glowing playhead on the timeline. One switch turns
    them all off (Settings > Appearance > Animations)
  - Custom theme (Settings > Custom theme): change every colour - window,
    sidebar, cards, boxes, text, lines, record / OK / warning colours - plus the
    second colour of the sweep, glow strength and glow size. Start from Neon,
    Dark or Light, and save looks under a name to switch between them
  - each animation also has its own switch (Settings > Animations)
  - nothing animates while the window is hidden or minimised, and the mini bar
    has no effects, so it costs nothing while a game is running

NEW IN 6
  - Sequences: chain short recordings, each one starting only after its picture
    was clicked; repeat a group of steps; start from any step
  - image steps can wait with no time limit, press a key instead of (or as well
    as) clicking, and give up early if a different picture shows
  - Pause / continue (F11) for recording and playback
  - timeline in the Editor, and "wait before this action" to fix one gap
  - run log: on screen, and as a .txt file per run
  - favorites in the Library
  - image steps can look inside one window only
  - the mini bar shows the time while playing
  - image clicks are more reliable in games: the pointer settles and nudges
    before the press, and the press is held for a few frames
