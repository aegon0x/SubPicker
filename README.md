# SubPicker

A Windows desktop app (Python / Tkinter) to download, pick, sync and export subtitles in MKVToolNix track order.

## Features

- **Download** subtitle packs from a subtitle site (URL and login set in Settings; the password is stored encrypted with Windows DPAPI).
- **Pick subtitles**
  - Grouped by language.
  - Provider priority list that you reorder by dragging (press and hold); double-click a provider to show only its files.
  - SDH and forced tracks are detected automatically from the subtitle text.
  - Drag & drop subtitle files, folders or MKVs onto the window.
- **From MKV**: extract subtitle tracks from any MKV into a folder you choose, then pick from them.
- **Track order**
  - Order the tracks the way MKVToolNix will, with track names, SDH / forced / default flags and delay.
  - Per-provider Source tag XMLs are filled in automatically.
  - A SYNC tick column chooses which tracks get synced.
- **Sync**
  - With alass or ffsubsync against an outside reference or one of the tracks.
  - Batch sync or a single file.
  - PGS (.sup) tracks are excluded from sync; use the delay column for those.
- **Export**: MKS, MKV (mkvmerge) or plain SRT files.

## Requirements

| Tool | Needed for |
|---|---|
| [MKVToolNix](https://mkvtoolnix.download/) | MKS / MKV export, extracting from MKV |
| [alass](https://github.com/kaegi/alass) or [ffsubsync](https://github.com/smacke/ffsubsync) | Sync |
| Python + `batchSync.py` | Batch sync (optional) |
| 7-Zip or WinRAR | Unpacking downloaded archives |

Tools on PATH or in their default install folders are found automatically; otherwise set them in **Settings**.

## Run from source

```
py SubPicker.py
```

## Build the portable exe

```
py -m pip install pyinstaller
py -m PyInstaller --noconfirm build\SubPicker.spec
```

The app is in `dist\SubPicker\`. Keep `SubPicker.exe` and the `_internal` folder together. Settings are saved in `settings.json` next to the exe; that file is personal and is git-ignored.
