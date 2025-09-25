# Virtual Desktop Shortcut

Keyboard shortcuts for switching between virtual desktops directly by number and moving windows between desktops.

## Features

- Switch to virtual desktops 1-9 using customizable key combinations
- Move the current window to a desktop and switch to it

## Installation

1. Download the executable from the releases page
2. Place it in a folder of your choice
3. Run the application
4. Use `Start-Process "vdshortcut.exe" -WindowStyle Hidden` to run in the background

## Configuration

On first run, the application will create a `config.jsonc` file in the same directory. You can customize the hotkeys by editing this file:

```jsonc
{
  // Switch desktop key combination (just switch to desktop)
  // F13 = 0x7C, LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5
  "switchKeys": [124], // F13 only (0x7C = 124)

  // Move window and switch key combination (move window to desktop and switch)
  // This should have more keys than switchKeys to have higher priority
  "moveKeys": [124, 160] // F13 + Left Shift (0x7C = 124, 0xA0 = 160)
}
```

## License

GPLv3.
