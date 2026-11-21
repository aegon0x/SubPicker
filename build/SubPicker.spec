# -*- mode: python ; coding: utf-8 -*-
# Build:  py -m PyInstaller --noconfirm build\SubPicker.spec   (run from the repo folder)
# Output: dist\SubPicker\  (SubPicker.exe + _internal - keep them together)
import os

HERE = os.path.abspath(SPECPATH)
ROOT = os.path.dirname(HERE)

a = Analysis(
    [os.path.join(ROOT, 'SubPicker.py')],
    pathex=[],
    binaries=[],
    datas=[],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['PIL', 'numpy'],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='SubPicker',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,                      # UPX packing makes antivirus false positives more likely
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    version=os.path.join(HERE, 'version.txt'),
    icon=[os.path.join(HERE, 'subpicker.ico')],
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name='SubPicker',
)
