Window Cloner
=============

A Windows-only program that allows you to create mirrors of any window on your
desktop via Desktop Window Manager (DWM). It renders a real-time thumbnail of a
target window inside a movable, resizable, always-on-top frame. You can crop,
scale, and reposition the mirror freely, then save the layout as a reusable
preset.


Note
----

- Often press Alt + F1 while the mirror window is focused to restore the target
  window focus in order for the display to not be freezed.
- This program has a CLI mode. That means that you can use stuff like cmd or bat
  files to create mirroring shortcuts.
- The layouts saved are always tied to the user's monitor resolution. It does
  not account for relativeness of resolutions in location, scaling and monitor
  indexes.


Why did I create this program?
------------------------------

I created this program in order to mirror my girlfriend's Telegram camera,
because you can't do that natively on Telegram.


Features
--------

- Window image mirroring - Uses DWM thumbnails for real-time rendering of the
  source window

- Custom capture region - Select exactly which part of the source window to
  mirror, via an interactive rectangle selector

- Free positioning and resizing - Move and resize the mirror frame like any
  normal window

- Modifier-keys while resizing:
  - Shift        - preserve the original aspect ratio
  - Ctrl         - resize symmetrically from the center
  - Alt          - scale the capture region proportionally with the frame
                   (with Shift), or crop/expand it (without Shift)

- System tray integration - App runs on background

- Layout presets - Save the current position, size, capture region, and scale to
  a named preset. Presets are stored in layouts.txt next to the executable

- Automatic layout matching - When selecting a region, Window Cloner filters
  available presets by window title and/or process name and warns you if the
  source window's size does not match the preset's expected size

- Undo / redo - Full history support (Ctrl+Z / Ctrl+Y) inside the rectangle
  selector

- Keyboard-friendly rectangle selector - Arrow keys to move or resize, Space to
  select everything, Enter to confirm, Esc to cancel

- CLI support - Launch mirrors directly from the command line, optionally
  applying a saved preset or an inline layout definition

- Target window management - The source window is temporarily moved offscreen
  while mirrored, and restored to its original placement when the mirror is
  closed or the app exits.


Requirements
------------

- Windows 10 or Windows 11 (DWM composition must be enabled)
- .NET 8
- A source window that is not protected against DWM thumbnail capture (some
  browsers and DRM-protected applications block this)


Building
--------

1. Clone the repository
2. Open the solution in Visual Studio 2022 or later
3. Restore NuGet packages
4. Build the project in Release mode
5. Run WindowCloner.exe


Usage
-----

Graphical mode
~~~~~~~~~~~~~~

Run WindowCloner.exe with no arguments. The Welcome window appears with a list
of currently open windows. Pick one, then:

1. The Rectangle Selector opens with a live screenshot of the target window
2. Drag to draw a capture region, or use the keyboard:
     Arrow keys       - move the selection (hold Ctrl for larger steps)
     Shift + arrows   - resize the selection
     Space            - select the entire window
     Ctrl+Z / Ctrl+Y  - undo / redo
     Enter            - confirm.  Esc: cancel
3. Optionally choose an existing preset from the dropdown. Presets are filtered
   by the target window's title and process name; toggle "Only match by window
   name" to narrow the filter
4. Click Confirm. The mirror appears, always being on top of your desktop

Resizing the mirror
~~~~~~~~~~~~~~~~~~~

Grab any edge or corner. While dragging:

    Shift                Preserve aspect ratio of the frame
    Ctrl                 Resize symmetrically from the center
    Alt                  Scale the capture region with the frame
    Alt + Shift          Scale the capture region proportionally to the frame
    Shift + Ctrl         Move a selected region
    (no modifier)        The capture region is fixed; only the frame changes
                         size

Keyboard shortcuts (mirror window)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    Alt+1       Toggle source window visibility (restore / hide)
    Alt+2       Reapply the saved layout
    Alt+`       Open the Save Layout dialog
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Command-line usage
------------------

    WindowCloner.exe <target> [<layout>] [-createPreset=<name>]

Target
~~~~~~

    w"Window Title"      Select the first window whose title matches
    e"processname"       Select the first window whose process name matches

~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Layout
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

    p"Preset Name"
        Apply a saved preset

    s"locX;locY;rectX;rectY;rectW;rectH[;scaleX;scaleY]"
        Apply an inline layout
        - Use -1;-1 for locX;locY to auto-center the frame on the source
          window's monitor
        - If scaleX and scaleY are omitted, 1.0 is used

~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Extra options
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

    -createPreset="Name"
        Instead of launching a mirror, save the current layout under the given
        name. If the name already exists, you are prompted to replace it

    -removePreset="Name"
        Remove a saved preset. Requires interactive confirmation

~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Examples
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

    :: Mirror a window by title, centered, no preset
    WindowCloner.exe w"Untitled - Notepad"

    :: Mirror a window by process name using a saved preset
    WindowCloner.exe e"notepad" p"My Layout"

    :: Inline layout: centered frame, source region at (100,50),
    :: 1200x780, no scaling
    WindowCloner.exe w"Untitled - Notepad" s"-1;-1;100;50;0;0;1200;780"

    :: Inline layout with 1.5x scale on both axes
    WindowCloner.exe w"Untitled - Notepad" s"-1;-1;100;50;0;0;1200;780;1.5;1.5"

    :: Save the current layout as a preset named "Wide"
    WindowCloner.exe w"Untitled - Notepad" -createPreset="Wide"

    :: Remove an existing preset
    WindowCloner.exe -removePreset="Wide"

~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Layout presets
--------------

Presets are stored as plain text in res/layouts.txt, in the same folder as the
executable. Each line is a single preset in the form:

    name=windowTitle;processName;locationX;locationY;rectX;rectY;rectW;rectH;width;height;scaleX;scaleY;bestSuitedForWidth;bestSuitedForHeight

Fields:

    name                    Preset name shown in the dropdown
    windowTitle             Expected window title for matching
    processName             Expected process name for matching
    locationX, locationY    Top-left position of the mirror frame on screen
    rectX, rectY, rectW,    Capture region within the source window
    rectH
    width, height           Size of the mirror frame
    scaleX, scaleY          Scaling factors applied to the capture region
    bestSuitedForWidth,     Source window size the preset was designed for
    bestSuitedForHeight

How it works
------------

- The mirror is a borderless, always-on-top WinForms window with a rounded
  region created via CreateRoundRectRgn

- A DWM thumbnail is registered with DwmRegisterThumbnail, linking the mirror
  window's client area to the source window

- DwmUpdateThumbnailProperties sets the source rectangle (rcSource) and the
  destination rectangle (rcDestination), which controls what part of the source
  is shown and how it is stretched into the mirror

- While the user drags edges, the app intercepts WM_SIZING, WM_MOVING,
  WM_ENTERSIZEMOVE, and WM_EXITSIZEMOVE to keep the capture rectangle and scale
  factors consistent with the frame geometry

- The source window is temporarily moved offscreen while mirrored and restored
  to its original WINDOWPLACEMENT when the mirror closes


Ideas for updates
-----------------

- Localization
- Global Settings
- Relativeness of resolutions with monitor indexes accounted in layouts


Project structure
-----------------
    Program.cs              Entry point, CLI parsing, layout file I/O, helpers
    Welcome.cs              Main window listing currently open windows
    RectangleSelector.cs    Interactive region selector with undo/redo and
                            keyboard support
    Form1.cs                The mirror window itself - DWM thumbnail host,
                            sizing logic, tray icon
    WindowLayoutObject.cs   Data model for a layout preset
    NativeMethods.cs        P/Invoke declarations for user32, dwmapi, gdi32.
    Win32Constants.cs       Win32 message and style constants
    SaveLayoutDialog.cs     Dialog for naming and saving a preset
    WindowCapture32.cs      Screenshot capture helper used by the rectangle
                            selector
