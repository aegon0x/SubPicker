#!/usr/bin/env python3
"""SubPicker - download, pick, sync and export subtitles in MKVToolNix track order.

Usage:  python SubPicker.py [folder]

Pages
  Download        search the copyparty subtitle site by TMDB / IMDb id ([id] in the folder
                  path) or title, download the folders as zip, extract (7-Zip / WinRAR) and load.
  Pick subtitles  primary provider (iTunes by default) per language, other providers fill the
                  gaps. Add subtitle files by hand or extract tracks from an MKV.
  Track order     MKVToolNix order, track names, flags, delay, Source tag, sync (alass /
                  ffsubsync, direct or through batchsync.py).
  Export bar      MKS / MKV / SRT files.
Settings (gear button): tool paths, site login, default folders.
"""
import os
import re
import sys
import json
import time
import base64
import queue
import shutil
import zipfile
import tempfile
import threading
import subprocess
import statistics
import urllib.parse
import urllib.request
import urllib.error
import tkinter as tk
import tkinter.font as tkfont
from tkinter import ttk, filedialog, messagebox
from xml.sax.saxutils import escape as xml_escape

SUB_EXTS = ('.srt', '.ass', '.ssa', '.vtt', '.sup')
ARCHIVE_EXTS = ('.zip', '.rar', '.7z')
NO_WINDOW = getattr(subprocess, 'CREATE_NO_WINDOW', 0)

PROVIDERS = {
    'it': 'iTunes', 'itunes': 'iTunes', 'atvp': 'Apple TV+', 'nf': 'Netflix', 'amzn': 'Amazon',
    'dsnp': 'Disney+', 'hmax': 'HBO Max', 'max': 'Max', 'pcok': 'Peacock', 'hulu': 'Hulu',
    'pmtp': 'Paramount+', 'megogo': 'MEGOGO', 'viap': 'Viaplay', 'mstp': 'Movistar+',
    'cgo': 'CGO', 'crav': 'Crave', 'stan': 'Stan', 'sky': 'SkyShowtime', 'skst': 'SkyShowtime',
    'hbo': 'HBO', 'roku': 'Roku', 'tubi': 'Tubi', 'mubi': 'MUBI', 'kinopoisk': 'Kinopoisk',
    'ivi': 'ivi', 'okko': 'Okko', 'zee5': 'ZEE5', 'jc': 'JioCinema', 'hs': 'Hotstar',
    'mi': 'Mediaset Infinity', 'hoop': 'Hoopla', 'hoopla': 'Hoopla', 'ma': 'Movies Anywhere',
}
DEFAULT_PRIORITY = ['iTunes', 'Blu-ray', 'MEGOGO', 'Netflix', 'Amazon', 'Disney+', 'Apple TV+',
                    'Max', 'HBO Max', 'Viaplay', 'CGO', 'Movistar+']
FLAG_WORDS = {'sdh', 'cc', 'hi', 'forced', 'full', 'signs'}
SDH_FLAGS = {'sdh', 'cc', 'hi'}

ISO3 = {
    'eng': 'en', 'fre': 'fr', 'fra': 'fr', 'ger': 'de', 'deu': 'de', 'spa': 'es', 'ita': 'it',
    'por': 'pt', 'rus': 'ru', 'jpn': 'ja', 'kor': 'ko', 'chi': 'zh', 'zho': 'zh', 'ara': 'ar',
    'heb': 'he', 'hin': 'hi', 'tha': 'th', 'tur': 'tr', 'pol': 'pl', 'dut': 'nl', 'nld': 'nl',
    'swe': 'sv', 'nor': 'no', 'nob': 'no', 'nb': 'no', 'dan': 'da', 'fin': 'fi', 'cze': 'cs',
    'ces': 'cs', 'slo': 'sk', 'slk': 'sk', 'hun': 'hu', 'gre': 'el', 'ell': 'el', 'ukr': 'uk',
    'vie': 'vi', 'ind': 'id', 'may': 'ms', 'msa': 'ms', 'rum': 'ro', 'ron': 'ro', 'bul': 'bg',
    'est': 'et', 'lav': 'lv', 'lit': 'lt', 'slv': 'sl', 'ice': 'is', 'isl': 'is', 'hrv': 'hr',
    'srp': 'sr', 'kaz': 'kk', 'kir': 'ky', 'aze': 'az', 'cat': 'ca', 'tam': 'ta', 'tel': 'te',
    'ben': 'bn', 'urd': 'ur', 'per': 'fa', 'fas': 'fa', 'fil': 'fil', 'tgl': 'fil', 'tl': 'fil',
    'yue': 'yue', 'baq': 'eu', 'eus': 'eu', 'glg': 'gl', 'mac': 'mk', 'mkd': 'mk', 'alb': 'sq',
    'sqi': 'sq', 'bos': 'bs', 'arm': 'hy', 'hye': 'hy', 'geo': 'ka', 'kat': 'ka', 'mal': 'ml',
    'kan': 'kn', 'mar': 'mr', 'gle': 'ga', 'wel': 'cy', 'cym': 'cy', 'khm': 'km', 'lao': 'lo',
    'bur': 'my', 'mya': 'my', 'sin': 'si', 'nep': 'ne', 'pan': 'pa', 'guj': 'gu', 'swa': 'sw',
    'afr': 'af', 'zul': 'zu', 'uzb': 'uz', 'bel': 'be', 'mon': 'mn', 'tib': 'bo', 'bod': 'bo',
    'uig': 'ug',
}
# English names as MediaInfo shows them (sorting uses these, so Kirghiz comes before Korean)
LANG_NAMES = {
    'af': 'Afrikaans', 'am': 'Amharic', 'ar': 'Arabic', 'az': 'Azerbaijani', 'be': 'Belarusian',
    'bg': 'Bulgarian', 'bn': 'Bengali', 'bo': 'Tibetan', 'bs': 'Bosnian', 'ca': 'Catalan',
    'cs': 'Czech', 'cy': 'Welsh', 'da': 'Danish', 'de': 'German', 'el': 'Greek', 'en': 'English',
    'es': 'Spanish', 'et': 'Estonian', 'eu': 'Basque', 'fa': 'Persian', 'fi': 'Finnish',
    'fil': 'Filipino', 'fr': 'French', 'ga': 'Irish', 'gl': 'Galician', 'gu': 'Gujarati',
    'he': 'Hebrew', 'hi': 'Hindi', 'hr': 'Croatian', 'hu': 'Hungarian', 'hy': 'Armenian',
    'id': 'Indonesian', 'is': 'Icelandic', 'it': 'Italian', 'ja': 'Japanese', 'ka': 'Georgian',
    'kk': 'Kazakh', 'km': 'Khmer', 'kn': 'Kannada', 'ko': 'Korean', 'ky': 'Kirghiz', 'lo': 'Lao',
    'lt': 'Lithuanian', 'lv': 'Latvian', 'mk': 'Macedonian', 'ml': 'Malayalam', 'mn': 'Mongolian',
    'mr': 'Marathi', 'ms': 'Malay', 'my': 'Burmese', 'ne': 'Nepali', 'nl': 'Dutch',
    'no': 'Norwegian', 'pa': 'Panjabi', 'pl': 'Polish', 'pt': 'Portuguese', 'ro': 'Romanian',
    'ru': 'Russian', 'si': 'Sinhala', 'sk': 'Slovak', 'sl': 'Slovenian', 'sq': 'Albanian',
    'sr': 'Serbian', 'sv': 'Swedish', 'sw': 'Swahili', 'ta': 'Tamil', 'te': 'Telugu', 'th': 'Thai',
    'tr': 'Turkish', 'ug': 'Uighur', 'uk': 'Ukrainian', 'ur': 'Urdu', 'uz': 'Uzbek',
    'vi': 'Vietnamese', 'yue': 'Cantonese', 'zh': 'Chinese', 'zu': 'Zulu', 'und': 'Unknown',
}
# Region variants per language, in MKV order: ((codes...), track title)
VARIANTS = {
    'en': [(('US', ''), ''), (('GB', 'UK'), 'British'), (('AU',), 'Australian'),
           (('CA',), 'Canadian'), (('IE',), 'Irish'), (('NZ',), 'New Zealand'), (('IN',), 'Indian')],
    'es': [(('ES',), 'Castilian'), (('',), ''), (('419',), 'Latin American'), (('MX',), 'Mexican')],
    'pt': [(('PT',), 'Iberian'), (('',), ''), (('BR',), 'Brazilian')],
    'fr': [(('FR',), 'European'), (('',), ''), (('CA',), 'Canadian'), (('BE',), 'Belgian'),
           (('CH',), 'Swiss')],
    'zh': [(('Hans', 'CN', 'SG'), 'Simplified'), (('',), ''), (('Hant', 'TW'), 'Traditional'),
           (('HK',), 'Traditional (Hong Kong)')],
    'yue': [(('Hant', 'HK'), 'Cantonese (Traditional)'), (('',), 'Cantonese'),
            (('Hans',), 'Cantonese (Simplified)')],
    'de': [(('DE', ''), ''), (('AT',), 'Austrian'), (('CH',), 'Swiss')],
    'nl': [(('NL', ''), ''), (('BE',), 'Flemish')],
    'sr': [(('', 'Latn'), 'Latin'), (('Cyrl',), 'Cyrillic')],
}

TIME_RE = re.compile(r'(?:\d+:)?\d{2}:\d{2}[,.]\d{3}\s*-->')
# Music notes are NOT counted: normal (non-SDH) subs mark song lyrics with them too.
SDH_RE = re.compile(r'\[[^\]]{2,}\]|\([^)]{2,}\)|^\s*-?\s*[A-Z][A-Z0-9 .\'\-]{1,25}:', re.M)
SDH_THRESHOLD = 0.05      # >= 5% of cues with (sounds), [sounds] or NAME: -> SDH
FORCED_RATIO = 0.20       # < 20% of the folder's median cue count -> forced
LANG_TAG_RE = re.compile(r'[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*')
SKIP_DIRS = {'selected', 'synced'}

C = dict(bg='#131518', panel='#1c1f24', field='#262a31', hover='#2f343c', border='#343a43',
         box_border='#5f6774', fg='#e6e8eb', muted='#8f97a2', faint='#5d6570', accent='#4b8bff',
         accent_hi='#6a9fff', accent_dn='#3a74e0', sel='#25406e', miss_bg='#33261a',
         miss_fg='#ffb46e', have_bg='#1b2b22', have_fg='#86e0a6', off_fg='#7d8590',
         stripe='#1a1d22', edit='#ffd479', pill_bg='#22355a', pill_fg='#a9c7ff',
         ok='#86e0a6', err='#ff7b7b', sync='#7fd6c2')


# ---------------------------------------------------------------- settings
def _which(*names):
    for n in names:
        p = shutil.which(n)
        if p:
            return p
    return ''


def _first_file(*paths):
    return next((p for p in paths if p and os.path.isfile(p)), '')


PF = os.environ.get('ProgramFiles', r'C:\Program Files')
PF86 = os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')


FROZEN = getattr(sys, 'frozen', False)                  # running as the portable SubPicker.exe
APP_DIR = os.path.dirname(os.path.abspath(sys.executable if FROZEN else __file__))


def _mkvtoolnix_dirs():
    """Folders where MKVToolNix may be installed: PATH, Program Files, the uninstall registry entry."""
    dirs = [os.path.dirname(p) for p in (_which('mkvextract'), _which('mkvmerge')) if p]
    dirs += [os.path.join(PF, 'MKVToolNix'), os.path.join(PF86, 'MKVToolNix')]
    try:
        import winreg
        for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            for sub in (r'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MKVToolNix',
                        r'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\MKVToolNix'):
                try:
                    with winreg.OpenKey(hive, sub) as k:
                        loc = winreg.QueryValueEx(k, 'InstallLocation')[0] or \
                            os.path.dirname(str(winreg.QueryValueEx(k, 'DisplayIcon')[0]).split(',')[0].strip('"'))
                        dirs.append(loc)
                except OSError:
                    pass
    except ImportError:
        pass
    return [d for d in dirs if d]


def find_mkv_tool(name, near=''):
    """mkvmerge / mkvextract: next to `near` (the other tool) first, then the usual install places."""
    dirs = ([os.path.dirname(near)] if near else []) + _mkvtoolnix_dirs()
    return _first_file(*[os.path.join(d, name + '.exe') for d in dirs])


def default_settings():
    here = APP_DIR
    return {
        'mkvmerge': find_mkv_tool('mkvmerge'),
        'mkvextract': find_mkv_tool('mkvextract'),
        'batchsync': _first_file(os.path.join(here, 'batchSync.py'), os.path.join(here, 'batchsync.py')),
        'alass': _which('alass', 'alass-cli'),
        'ffsubsync': _which('ffsubsync'),
        'sevenzip': _which('7z') or _first_file(os.path.join(PF, '7-Zip', '7z.exe'),
                                                 os.path.join(PF86, '7-Zip', '7z.exe')),
        'winrar': _first_file(os.path.join(PF, 'WinRAR', 'WinRAR.exe'), os.path.join(PF86, 'WinRAR', 'WinRAR.exe')),
        'site_url': '',             # set in Settings (kept out of the code so the app can be shared)
        'site_user': '',
        'site_pass': '',
        'download_dir': '',
        'sync_tool': 'alass',
        'sync_method': 'batch',
        'ref_dir': '',
        'tags_dir': '',
        'source_tags': {},          # provider code / name (lower case) -> Source tags XML
    }


TOOL_KEYS = ('mkvmerge', 'mkvextract', 'batchsync', 'alass', 'ffsubsync', 'sevenzip', 'winrar')


def python_cmd():
    """Interpreter used to run batchsync.py (no setting needed)."""
    exe = _which('py', 'python')
    if exe:
        return exe
    if FROZEN:
        return 'py'
    console = os.path.join(os.path.dirname(sys.executable), 'python.exe')    # not pythonw
    return console if os.path.isfile(console) else sys.executable


def legacy_settings_path():
    base = os.environ.get('APPDATA') or os.path.expanduser('~')
    return os.path.join(base, 'SubPicker', 'settings.json')


def settings_path():
    """Portable exe: settings.json next to SubPicker.exe. Script: %APPDATA%\\SubPicker."""
    return os.path.join(APP_DIR, 'settings.json') if FROZEN else legacy_settings_path()


def _dpapi(data, encrypt):
    """Windows DPAPI (current user) encrypt / decrypt."""
    import ctypes
    from ctypes import wintypes

    class BLOB(ctypes.Structure):
        _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_char))]

    buf = ctypes.create_string_buffer(data, len(data))
    blob_in = BLOB(len(data), ctypes.cast(buf, ctypes.POINTER(ctypes.c_char)))
    blob_out = BLOB()
    fn = ctypes.windll.crypt32.CryptProtectData if encrypt else ctypes.windll.crypt32.CryptUnprotectData
    if not fn(ctypes.byref(blob_in), None, None, None, None, 0, ctypes.byref(blob_out)):
        raise OSError('DPAPI failed')
    try:
        return ctypes.string_at(blob_out.pbData, blob_out.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(blob_out.pbData)


def protect(text):
    if not text:
        return ''
    try:
        return 'dpapi:' + base64.b64encode(_dpapi(text.encode('utf-8'), True)).decode()
    except Exception:
        return 'b64:' + base64.b64encode(text.encode('utf-8')).decode()


def unprotect(value):
    try:
        if value.startswith('dpapi:'):
            return _dpapi(base64.b64decode(value[6:]), False).decode('utf-8')
        if value.startswith('b64:'):
            return base64.b64decode(value[4:]).decode('utf-8')
    except Exception:
        return ''
    return value


def load_settings():
    cfg = default_settings()
    path = settings_path()
    if not os.path.isfile(path) and os.path.isfile(legacy_settings_path()):
        path = legacy_settings_path()                 # first run of the exe: carry the old settings over
    try:
        with open(path, encoding='utf-8') as fh:
            saved = json.load(fh)
        cfg.update({k: v for k, v in saved.items() if v not in (None,) and (v != '' or k.startswith('site'))})
    except (OSError, ValueError):
        pass
    detected = default_settings()
    for k in TOOL_KEYS:                       # saved path gone / never set -> take it from PATH
        if not resolve_tool(cfg.get(k)) and detected.get(k):
            cfg[k] = detected[k]
    cfg.pop('python', None)
    return cfg


def save_settings(cfg):
    os.makedirs(os.path.dirname(settings_path()), exist_ok=True)
    with open(settings_path(), 'w', encoding='utf-8') as fh:
        json.dump(cfg, fh, indent=1)


def resolve_tool(value):
    """Path or command name -> runnable path ('' if not found)."""
    if not value:
        return ''
    if os.path.isfile(value):
        return value
    return shutil.which(value) or ''


# ---------------------------------------------------------------- provider source tags
TAG_FILE_RE = re.compile(r'^(.+?)\s+Source Tag\.xml$', re.I)


def provider_label(code):
    """'flmn' -> 'Filmin', 'Megogo' -> 'MEGOGO', unknown -> as typed."""
    low = code.strip().lower()
    if low in PROVIDERS:
        return PROVIDERS[low]
    return next((v for v in PROVIDERS.values() if v.lower() == low), code.strip())


def known_provider(code):
    low = code.strip().lower()
    return low in PROVIDERS or any(v.lower() == low for v in PROVIDERS.values())


def provider_keys(sub):
    """Every lower-case name a subtitle's provider can be listed under: raw code, full name, aliases."""
    keys = {sub.provider.lower()}
    raw = provider_code(os.path.splitext(sub.name)[0])
    if raw:
        keys.add(raw.lower())
    keys |= {k for k, v in PROVIDERS.items() if v == sub.provider}
    if sub.provider == 'Blu-ray':
        keys |= {'bluray', 'blu-ray', 'bd'}
    return keys


def scan_tags_dir(folder):
    """'<code> Source Tag.xml' files -> ({code: path} for known providers, [names not recognised])."""
    found, unknown = {}, []
    try:
        names = sorted(os.listdir(folder))
    except OSError:
        return found, unknown
    for fn in names:
        m = TAG_FILE_RE.match(fn)
        if not m:
            continue
        code = m.group(1).strip()
        if known_provider(code):
            found[code] = os.path.join(folder, fn)
        else:
            unknown.append(code)
    return found, unknown


# ---------------------------------------------------------------- parsing
class Sub:
    def __init__(self, path):
        self.path = path
        self.name = os.path.basename(path)
        stem = os.path.splitext(self.name)[0]
        self.provider = detect_provider(stem)
        self.tag, self.flags = parse_lang(stem)
        base = self.tag.split('-')[0].lower()
        self.base = ISO3.get(base, base)
        if '-' not in self.tag and self.tag != self.base:          # eng -> en
            self.tag = self.base
        self.region = self.tag.split('-', 1)[1] if '-' in self.tag else ''
        self.size = os.path.getsize(path)
        self.pgs = path.lower().endswith('.sup')
        self.auto = set()            # flags detected from the subtitle content
        if self.pgs:
            self.cues, forced_share = pgs_info(path)
            self.sdh_ratio = 0.0
            if 'forced' not in self.flags and self.cues and forced_share >= 0.9:
                self.auto.add('forced')                     # PGS objects carry a forced flag
        else:
            self.cues, self.sdh_ratio = analyse(path)
        if not self.flags & SDH_FLAGS and self.sdh_ratio >= SDH_THRESHOLD:
            self.auto.add('sdh')

    @property
    def is_sdh(self):
        return bool((self.flags | self.auto) & SDH_FLAGS)

    @property
    def is_forced(self):
        return 'forced' in (self.flags | self.auto)

    @property
    def variant(self):
        s = self.tag + ('  [PGS]' if self.pgs else '')
        for f in sorted(self.flags):
            s += f'  [{f.upper()}]'
        for f in sorted(self.auto):
            s += f'  [{f.upper()} auto]'
        return s


def provider_code(stem):
    """Raw provider tag in front of WEB (e.g. 'BB' in Movie.BB.WEB.en.srt) or None."""
    m = re.search(r'[.\-_ ]([A-Za-z0-9+]+)[.\-_ ](?:WEB|WEB-DL|WEBRip)\b', stem, re.I)
    return m.group(1) if m else None


def detect_provider(stem):
    if re.search(r'blu-?ray|bdrip|bdremux|remux|uhd[.\- ]?bd|\bbd\b', stem, re.I):
        return 'Blu-ray'
    m = re.search(r'[.\-_ ]([A-Za-z0-9+]+)[.\-_ ](?:WEB|WEB-DL|WEBRip)\b', stem, re.I)
    if m:
        return PROVIDERS.get(m.group(1).lower(), m.group(1).upper())
    for tok in re.split(r'[.\-_ \[\]]+', stem):
        if len(tok) > 2 and tok.lower() in PROVIDERS:
            return PROVIDERS[tok.lower()]
    return 'Unknown'


def parse_lang(stem):
    flags = set()
    toks = stem.split('.')
    while len(toks) > 1 and toks[-1].lower() in FLAG_WORDS - {'hi'}:   # bare ".hi" = Hindi
        flags.add(toks.pop().lower())
    last = toks[-1].split('_')[-1]                    # ..._track3_[eng] -> [eng]
    for f in re.findall(r'\[([A-Za-z]+)\]', last):
        if f.lower() in FLAG_WORDS:
            flags.add(f.lower())
    last = re.sub(r'\[(?:%s)\]' % '|'.join(FLAG_WORDS), '', last, flags=re.I)
    last = last.strip('[]() ')
    last = re.sub(r'-\d{1,2}$', '', last)             # duplicate suffix "-1" (keeps es-419)
    if not LANG_TAG_RE.fullmatch(last):
        return 'und', flags
    parts = last.split('-')
    parts[0] = parts[0].lower()
    for i in range(1, len(parts)):                    # pt-br -> pt-BR, zh-hant -> zh-Hant
        p = parts[i]
        if p.isalpha() and len(p) == 2:
            parts[i] = p.upper()
        elif p.isalpha() and len(p) == 4:
            parts[i] = p.title()
    return '-'.join(parts), flags


def pgs_segments(data):
    """Yield (offset, type, payload_start, payload_size) for each PGS segment ('PG' header)."""
    i, n = 0, len(data)
    while i + 13 <= n and data[i:i + 2] == b'PG':
        size = int.from_bytes(data[i + 11:i + 13], 'big')
        yield i, data[i + 10], i + 13, size
        i += 13 + size


def pgs_info(path):
    """PGS/.sup: (subtitle events, share of events whose objects all carry the forced flag)."""
    try:
        with open(path, 'rb') as fh:
            data = fh.read()
    except OSError:
        return 0, 0.0
    events = forced = 0
    for _, typ, p, size in pgs_segments(data):
        if typ != 0x16 or size < 11:                        # PCS
            continue
        nobj = data[p + 10]
        if not nobj:                                        # empty composition = clear screen
            continue
        events += 1
        q, all_forced = p + 11, True
        for _ in range(nobj):
            if q + 8 > p + size:
                break
            flags = data[q + 3]
            all_forced &= bool(flags & 0x40)
            q += 8 + (8 if flags & 0x80 else 0)
        forced += all_forced
    return events, (forced / events if events else 0.0)


def shift_pgs(src, dst, ms):
    """Copy a .sup moving every PTS/DTS (90 kHz) by ms milliseconds."""
    data = bytearray(open(src, 'rb').read())
    d = ms * 90
    for off, _, _, _ in list(pgs_segments(bytes(data))):
        for k in (off + 2, off + 6):
            v = int.from_bytes(data[k:k + 4], 'big')
            if v or k == off + 2:                           # DTS 0 = unused, keep it
                data[k:k + 4] = (max(0, v + d) & 0xFFFFFFFF).to_bytes(4, 'big')
    with open(dst, 'wb') as fh:
        fh.write(data)


def analyse(path):
    """Return (cue count, share of cues that look like SDH). PGS: events counted, SDH unknown."""
    if path.lower().endswith('.sup'):
        return pgs_info(path)[0], 0.0
    try:
        with open(path, encoding='utf-8-sig', errors='replace') as fh:
            data = fh.read()
    except OSError:
        return 0, 0.0
    if path.lower().endswith(('.ass', '.ssa')):
        texts = [re.sub(r'\{[^}]*\}', '', ln.split(',', 9)[-1]).replace('\\N', '\n')
                 for ln in data.splitlines() if ln.startswith('Dialogue:')]
    else:
        data = data.replace('\r\n', '\n').replace('\r', '\n')
        texts = []
        for block in re.split(r'\n\s*\n', data):
            m = TIME_RE.search(block)
            if m:
                body = block[m.end():].split('\n', 1)
                texts.append(re.sub(r'<[^>]+>', '', body[1] if len(body) > 1 else ''))
    n = len(texts)
    return n, (sum(1 for t in texts if SDH_RE.search(t)) / n if n else 0.0)


def shift_file(src, dst, ms):
    """Copy a subtitle file, moving every timestamp by ms milliseconds."""
    with open(src, 'rb') as fh:
        raw = fh.read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig', errors='replace')

    if src.lower().endswith(('.ass', '.ssa')):
        def rep(m):
            t = max(0, ((int(m[1]) * 60 + int(m[2])) * 60 + int(m[3])) * 1000 + int(m[4]) * 10 + ms)
            cs = t // 10
            h, r = divmod(cs, 360000)
            mi, r = divmod(r, 6000)
            s, c = divmod(r, 100)
            return f'{h}:{mi:02d}:{s:02d}.{c:02d}'

        def fix(line):
            if not line.startswith('Dialogue:'):
                return line
            return re.sub(r'(\d+):(\d{2}):(\d{2})\.(\d{2})', rep, line, count=2)
    else:
        def rep(m):
            h = int(m[1]) if m[1] else 0
            t = max(0, ((h * 60 + int(m[2])) * 60 + int(m[3])) * 1000 + int(m[5]) + ms)
            hh, r = divmod(t, 3600000)
            mm, r = divmod(r, 60000)
            ss, ff = divmod(r, 1000)
            return f'{hh:02d}:{mm:02d}:{ss:02d}{m[4]}{ff:03d}'

        def fix(line):
            if '-->' not in line:
                return line
            return re.sub(r'(?:(\d+):)?(\d{2}):(\d{2})([,.])(\d{3})', rep, line)

    text = '\n'.join(fix(ln) for ln in text.split('\n'))
    with open(dst, 'wb') as fh:
        fh.write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))


def is_xml_path(v):
    return bool(v) and v.lower().endswith('.xml') and ('\\' in v or '/' in v or os.path.isfile(v))


def source_xml(text):
    return ('<?xml version="1.0" encoding="UTF-8"?>\n<Tags>\n  <Tag>\n    <Targets />\n    <Simple>\n'
            '      <Name>Source</Name>\n'
            f'      <String>{xml_escape(text)}</String>\n'
            '    </Simple>\n  </Tag>\n</Tags>\n')


def lang_name(base):
    return LANG_NAMES.get(base, base)


def family(base):
    return 'Chinese' if base == 'yue' else lang_name(base)


def lang_display(tag):
    base = ISO3.get(tag.split('-')[0].lower(), tag.split('-')[0].lower())
    region = tag.split('-', 1)[1] if '-' in tag else ''
    region = {'hans': 'Simplified', 'hant': 'Traditional', '419': 'Latin America',
              'latn': 'Latin', 'cyrl': 'Cyrillic'}.get(region.lower(), region)
    return lang_name(base) + (f' ({region})' if region else '')


def region_info(s):
    """(rank, track title, unknown-region key) of a sub's region variant."""
    table = VARIANTS.get(s.base)
    reg = s.region
    if table:
        cands = [reg] + ([reg.split('-')[0]] if '-' in reg else [])
        for c in cands:
            for i, (codes, title) in enumerate(table):
                if c.lower() in (x.lower() for x in codes):
                    return i, title, ''
        return len(table), '', reg.lower()
    return (0, '', '') if not reg else (1, '', reg.lower())


def common_title(names, fallback):
    pre = os.path.commonprefix(names) if names else ''
    pre = pre[:pre.rfind('.')] if '.' in pre else ''
    return pre.strip('._- ') or fallback


def list_subs(folder):
    """Subtitle files in folder and its sub-folders (export folders skipped)."""
    out = []
    for root, dirs, files in os.walk(folder):
        dirs[:] = sorted(d for d in dirs if d.lower() not in SKIP_DIRS and not d.startswith(('.', '_')))
        out += [os.path.join(root, f) for f in sorted(files) if f.lower().endswith(SUB_EXTS)]
    return out


def unique_path(path):
    if not os.path.exists(path):
        return path
    stem, ext = os.path.splitext(path)
    k = 2
    while os.path.exists(f'{stem}_{k}{ext}'):
        k += 1
    return f'{stem}_{k}{ext}'


# ---------------------------------------------------------------- subtitle site (copyparty)
class SiteClient:
    UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) SubPicker/2.0'

    def __init__(self, url, user, password):
        self.base = url.rstrip('/')
        self.pw = f'{user}:{password}' if user else password

    def _open(self, path, data=None, ctype=None, timeout=60):
        req = urllib.request.Request(self.base + path, data=data, headers={'User-Agent': self.UA})
        if self.pw:
            req.add_header('PW', self.pw)
        if ctype:
            req.add_header('Content-Type', ctype)
        return urllib.request.urlopen(req, timeout=timeout)

    @staticmethod
    def _err(e):
        try:
            body = e.read().decode('utf-8', 'replace')
        except Exception:
            body = ''
        body = re.sub(r'<[^>]+>', ' ', body).strip()
        return f'HTTP {e.code}: {body[:200] or e.reason}'

    def search(self, query, n=5000):
        payload = json.dumps({'q': query, 'n': n}).encode()
        for attempt in range(6):
            try:
                with self._open('/?srch', payload, 'text/plain') as r:
                    return json.load(r)
            except urllib.error.HTTPError as e:
                if e.code == 429 and attempt < 5:          # server rate limit
                    time.sleep(1.2)
                    continue
                if e.code in (401, 403):
                    raise RuntimeError('Login failed - check the username / password in Settings.')
                raise RuntimeError(self._err(e))

    def download(self, url_path, dst, progress=None):
        try:
            with self._open(url_path, timeout=120) as r, open(dst, 'wb') as fh:
                total = int(r.headers.get('Content-Length') or 0)
                got = 0
                while True:
                    chunk = r.read(256 * 1024)
                    if not chunk:
                        break
                    fh.write(chunk)
                    got += len(chunk)
                    if progress:
                        progress(got, total)
        except urllib.error.HTTPError as e:
            raise RuntimeError(self._err(e))


def build_query(text):
    """'1698863', '[1698863]', 'tt1234567' -> id search; anything else -> path contains text."""
    t = text.strip()
    m = re.fullmatch(r'\[?\s*(tt\d+|\d+)\s*\]?', t, re.I)
    if m:
        return f'path like *[{m.group(1)}]*', f'[{m.group(1)}]'
    t = t.replace('"', '')
    return f'path like "*{t}*"', t


def group_hits(hits, needle):
    """Group search hits by the folder whose name contains the needle (the [id] folder)."""
    groups = {}
    nl = needle.lower()
    for h in hits:
        rp = urllib.parse.unquote(h['rp'].split('?')[0])
        parts = rp.split('/')
        idx = next((i for i, p in enumerate(parts[:-1]) if nl in p.lower()), len(parts) - 2)
        root = '/'.join(parts[:idx + 1])
        g = groups.setdefault(root, {'files': 0, 'size': 0, 'sub': {}})
        g['files'] += 1
        g['size'] += h.get('sz', 0)
        subdir = '/'.join(parts[idx + 1:-1]) or '·'
        g['sub'][subdir] = g['sub'].get(subdir, 0) + 1
    return groups


def extract_archive(arc, dest, cfg, log):
    os.makedirs(dest, exist_ok=True)
    sz = resolve_tool(cfg.get('sevenzip'))
    if sz:
        p = subprocess.run([sz, 'x', '-y', f'-o{dest}', arc], capture_output=True, text=True,
                           errors='replace', creationflags=NO_WINDOW)
        if p.returncode == 0:
            return True
        log(f'7-Zip failed on {os.path.basename(arc)}: {p.stdout.strip()[-200:]}', 'err')
    wr = resolve_tool(cfg.get('winrar'))
    if wr:
        p = subprocess.run([wr, 'x', '-ibck', '-y', '-o+', arc, dest + os.sep], capture_output=True,
                           creationflags=NO_WINDOW)
        if p.returncode == 0:
            return True
        log(f'WinRAR failed on {os.path.basename(arc)} (code {p.returncode})', 'err')
    if arc.lower().endswith('.zip'):
        with zipfile.ZipFile(arc) as z:
            z.extractall(dest)
        return True
    return False


# ---------------------------------------------------------------- drawing helpers
def _rgb(c):
    return tuple(int(c[i:i + 2], 16) for i in (1, 3, 5))


def make_box(state, bg, size):
    """Anti-aliased rounded tick box drawn into a PhotoImage (no PIL needed)."""
    S = 3
    bgc, acc, brd, fld, wht = map(_rgb, (bg, C['accent'], C['box_border'], C['field'], '#ffffff'))
    pad = size * 0.1
    x0 = y0 = pad
    x1 = y1 = size - pad
    rad = size * 0.22
    bw = max(1.2, size / 12)
    th = size * 0.08
    shapes = {'on': [((.28, .52), (.44, .68)), ((.44, .68), (.74, .35))],
              'part': [((.30, .5), (.70, .5))]}.get(state, [])
    segs = [((a[0] * size, a[1] * size), (b[0] * size, b[1] * size)) for a, b in shapes]

    def inside(x, y, ins):
        ax0, ay0, ax1, ay1 = x0 + ins, y0 + ins, x1 - ins, y1 - ins
        rr = max(rad - ins, 0.5)
        if x < ax0 or x > ax1 or y < ay0 or y > ay1:
            return False
        cx = min(max(x, ax0 + rr), ax1 - rr)
        cy = min(max(y, ay0 + rr), ay1 - rr)
        return (x - cx) ** 2 + (y - cy) ** 2 <= rr * rr

    def dseg(px, py, a, b):
        (ax, ay), (bx, by) = a, b
        dx, dy = bx - ax, by - ay
        t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
        return ((px - ax - t * dx) ** 2 + (py - ay - t * dy) ** 2) ** 0.5

    rows = []
    for y in range(size):
        row = []
        for x in range(size):
            r = g = b = 0
            for sy in range(S):
                for sx in range(S):
                    px, py = x + (sx + .5) / S, y + (sy + .5) / S
                    if not inside(px, py, 0):
                        c = bgc
                    elif state == 'off':
                        c = fld if inside(px, py, bw) else brd
                    else:
                        c = wht if any(dseg(px, py, a, b_) <= th for a, b_ in segs) else acc
                    r += c[0]; g += c[1]; b += c[2]
            n = S * S
            row.append('#%02x%02x%02x' % (r // n, g // n, b // n))
        rows.append('{' + ' '.join(row) + '}')
    img = tk.PhotoImage(width=size, height=size)
    img.put(' '.join(rows))
    return img


def rrect(cv, x1, y1, x2, y2, r, **kw):
    """Rounded rectangle on a Canvas."""
    r = max(0, min(r, (x2 - x1) / 2, (y2 - y1) / 2))
    pts = [x1 + r, y1, x2 - r, y1, x2, y1, x2, y1 + r, x2, y2 - r, x2, y2, x2 - r, y2,
           x1 + r, y2, x1, y2, x1, y2 - r, x1, y1 + r, x1, y1]
    return cv.create_polygon(pts, smooth=True, **kw)


def fit(font, text, w):
    """Shorten text with an ellipsis so it fits w pixels."""
    if w <= 10:
        return ''
    if font.measure(text) <= w:
        return text
    lo, hi = 0, len(text)
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if font.measure(text[:mid] + '…') <= w:
            lo = mid
        else:
            hi = mid - 1
    return text[:lo] + '…'


def dark_titlebar(win):
    if sys.platform != 'win32':
        return
    try:
        import ctypes
        win.update_idletasks()
        hwnd = ctypes.windll.user32.GetParent(win.winfo_id())
        val = ctypes.c_int(1)
        for attr in (20, 19):
            if ctypes.windll.dwmapi.DwmSetWindowAttribute(hwnd, attr, ctypes.byref(val),
                                                          ctypes.sizeof(val)) == 0:
                break
    except Exception:
        pass


def enable_file_drop(win, callback):
    """Accept files dropped from Explorer (WM_DROPFILES) - no extra packages needed.
    callback(list_of_paths) runs inside the window procedure, so it must not call Tk -
    hand the paths to a queue instead."""
    if sys.platform != 'win32':
        return False
    try:
        import ctypes
        from ctypes import wintypes
        user32, shell32 = ctypes.windll.user32, ctypes.windll.shell32
        LRESULT = ctypes.c_ssize_t
        WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)
        user32.CallWindowProcW.argtypes = [ctypes.c_void_p, wintypes.HWND, wintypes.UINT,
                                           wintypes.WPARAM, wintypes.LPARAM]
        user32.CallWindowProcW.restype = LRESULT
        user32.SetWindowLongPtrW.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_void_p]
        user32.SetWindowLongPtrW.restype = ctypes.c_void_p
        user32.GetParent.argtypes = [wintypes.HWND]
        user32.GetParent.restype = wintypes.HWND
        shell32.DragQueryFileW.argtypes = [ctypes.c_void_p, wintypes.UINT, ctypes.c_wchar_p, wintypes.UINT]
        shell32.DragQueryFileW.restype = wintypes.UINT
        shell32.DragFinish.argtypes = [ctypes.c_void_p]
        shell32.DragAcceptFiles.argtypes = [wintypes.HWND, wintypes.BOOL]
        win.update_idletasks()
        hwnd = user32.GetParent(win.winfo_id())
        old = {}

        def proc(h, msg, wp, lp):
            if msg == 0x0233:                                   # WM_DROPFILES
                paths = []
                try:
                    for i in range(shell32.DragQueryFileW(wp, 0xFFFFFFFF, None, 0)):
                        n = shell32.DragQueryFileW(wp, i, None, 0)
                        buf = ctypes.create_unicode_buffer(n + 1)
                        shell32.DragQueryFileW(wp, i, buf, n + 1)
                        paths.append(buf.value)
                finally:
                    shell32.DragFinish(wp)
                callback(paths)              # must not touch Tk here - callback only queues
                return 0
            return user32.CallWindowProcW(old['proc'], h, msg, wp, lp)

        cb = WNDPROC(proc)
        win._drop_proc = cb                                     # keep the callback alive
        old['proc'] = user32.SetWindowLongPtrW(hwnd, -4, ctypes.cast(cb, ctypes.c_void_p))  # GWLP_WNDPROC
        shell32.DragAcceptFiles(hwnd, True)
        return True
    except Exception as ex:
        print('drag & drop unavailable:', ex, file=sys.stderr)
        return False


def card(parent):
    """Bordered, filled panel."""
    return tk.Frame(parent, bg=C['panel'], highlightbackground=C['border'], highlightcolor=C['border'],
                    highlightthickness=1, bd=0)


class ScrollFrame(tk.Frame):
    """Put widgets in `.inner`. It fills the visible area like a normal frame (so expanding widgets still
    expand), and a vertical scrollbar appears only when the content is taller than the space available."""

    SELF_SCROLLING = (tk.Text, tk.Listbox, ttk.Treeview)

    def __init__(self, master, bg, fit_width=False):
        super().__init__(master, bg=bg, bd=0, highlightthickness=0)
        self.fit_width = fit_width          # True: as wide as the content (side panels)
        self.canvas = tk.Canvas(self, bg=bg, bd=0, highlightthickness=0, yscrollincrement=20,
                                width=1, height=1)
        self.vsb = ttk.Scrollbar(self, orient='vertical', command=self.canvas.yview)
        self.canvas.configure(yscrollcommand=self.vsb.set)
        self.canvas.pack(side='left', fill='both', expand=True)
        self.inner = tk.Frame(self.canvas, bg=bg, bd=0, highlightthickness=0)
        self.win = self.canvas.create_window(0, 0, window=self.inner, anchor='nw')
        self._last = None
        self.canvas.bind('<Configure>', lambda e: self._fit())
        self.winfo_toplevel().bind('<MouseWheel>', self._wheel, add='+')
        self._poll()

    def _poll(self):                       # content can grow later (tabs, rows added) -> re-fit
        if self.winfo_exists():
            self._fit()
            self.after(300, self._poll)

    def _fit(self):
        if self.fit_width and int(self.canvas.cget('width')) != self.inner.winfo_reqwidth():
            self.canvas.configure(width=self.inner.winfo_reqwidth())
        cw, ch = self.canvas.winfo_width(), self.canvas.winfo_height()
        if cw < 2 or ch < 2:
            return
        req = self.inner.winfo_reqheight()
        key = (cw, ch, req)
        if key == self._last:
            return
        self._last = key
        h = max(ch, req)
        self.canvas.itemconfigure(self.win, width=cw, height=h)
        self.canvas.configure(scrollregion=(0, 0, cw, h))
        if req > ch + 1:
            if not self.vsb.winfo_ismapped():
                self.vsb.pack(side='right', fill='y', before=self.canvas)
        else:
            if self.vsb.winfo_ismapped():
                self.vsb.pack_forget()
            self.canvas.yview_moveto(0)

    def overflowing(self):
        return self.inner.winfo_reqheight() > self.canvas.winfo_height() + 1

    def _wheel(self, e):
        try:
            w = self.winfo_containing(e.x_root, e.y_root)
        except (KeyError, tk.TclError):
            return
        inside = False
        while w is not None:
            if isinstance(w, self.SELF_SCROLLING) or isinstance(w, Table):
                return                     # that widget scrolls itself
            if isinstance(w, ScrollFrame) and w is not self and w.overflowing():
                return                     # an inner scroll area takes the wheel
            if w is self:
                inside = True
                break
            w = w.master
        if inside and self.overflowing():
            self.canvas.yview_scroll(int(-e.delta / 40), 'units')


def log_text(parent, height=8, font=None):
    t = tk.Text(parent, height=height, bg=C['bg'], fg=C['muted'], insertbackground=C['fg'], relief='flat',
                bd=0, highlightthickness=0, wrap='word', padx=10, pady=8, font=font, state='disabled')
    t.tag_configure('ok', foreground=C['ok'])
    t.tag_configure('err', foreground=C['err'])
    t.tag_configure('head', foreground=C['fg'])
    return t


def text_write(widget, text, tag=None):
    widget.config(state='normal')
    widget.insert('end', text + '\n', tag or ())
    widget.see('end')
    widget.config(state='disabled')


# ---------------------------------------------------------------- custom widgets
class Segmented(tk.Canvas):
    """Pill-shaped segmented control bound to a StringVar."""

    def __init__(self, master, app, options, variable, command=None, bg=None):
        self.app = app
        s = app.scale
        self.opts = list(options)
        self.var = variable
        self.cmd = command
        self.font = tkfont.Font(font=app.f_bold)
        self.h = int(30 * s)
        self.padx = int(16 * s)
        self.pad = int(3 * s)
        self.hover = None
        super().__init__(master, height=self.h, bg=bg or C['bg'], highlightthickness=0, bd=0, cursor='hand2')
        self._measure()
        self.bind('<Button-1>', self._click)
        self.bind('<Motion>', self._motion)
        self.bind('<Leave>', lambda e: self._set_hover(None))
        variable.trace_add('write', lambda *a: self.draw())
        self.draw()

    def _measure(self):
        self.ws = [self.font.measure(lbl) + 2 * self.padx for _, lbl in self.opts]
        self.configure(width=sum(self.ws) + 2 * self.pad)

    def set_label(self, value, label):
        self.opts = [(v, label if v == value else l) for v, l in self.opts]
        self._measure()
        self.draw()

    def draw(self):
        self.delete('all')
        W = sum(self.ws) + 2 * self.pad
        rrect(self, 0, 0, W - 1, self.h - 1, self.h / 2.4, fill=C['field'], outline=C['border'])
        x = self.pad
        for (val, lbl), w in zip(self.opts, self.ws):
            active = self.var.get() == val
            if active or self.hover == val:
                rrect(self, x, self.pad, x + w, self.h - self.pad - 1, (self.h - 2 * self.pad) / 2.4,
                      fill=C['accent'] if active else C['hover'], outline='')
            self.create_text(x + w / 2, self.h / 2, text=lbl, font=self.font,
                             fill='#ffffff' if active else (C['fg'] if self.hover == val else C['muted']))
            x += w

    def _at(self, x):
        cx = self.pad
        for (val, _), w in zip(self.opts, self.ws):
            if cx <= x < cx + w:
                return val
            cx += w
        return None

    def _set_hover(self, v):
        if v != self.hover:
            self.hover = v
            self.draw()

    def _motion(self, e):
        self._set_hover(self._at(e.x))

    def _click(self, e):
        v = self._at(e.x)
        if v is not None and v != self.var.get():
            self.var.set(v)
            if self.cmd:
                self.cmd()


class Table(tk.Frame):
    """Canvas-drawn table with tick boxes, boxed input fields and pills."""

    def __init__(self, master, app, columns):
        super().__init__(master, bg=C['panel'], bd=0, highlightthickness=0)
        self.app = app
        s = app.scale
        self.cols = columns
        self.rows = []
        self.sel = set()
        self.anchor = None
        self.rh = int(34 * s)
        self.hh = int(30 * s)
        self.font = tkfont.Font(font=app.f_base)
        self.head_font = tkfont.Font(font=app.f_small_bold)
        self.pill_font = tkfont.Font(font=app.f_small_bold)
        fams = tkfont.families(self)
        if 'Segoe Fluent Icons' in fams:
            self.icon_font, self.icon = tkfont.Font(family='Segoe Fluent Icons', size=10), '\uE838'
        elif 'Segoe MDL2 Assets' in fams:
            self.icon_font, self.icon = tkfont.Font(family='Segoe MDL2 Assets', size=10), '\uE838'
        else:
            self.icon_font, self.icon = self.font, '…'
        self.bw = int(28 * s)          # width of the browse button inside a field
        self.on_toggle = self.on_edit = self.on_open = self.on_menu = self.on_browse = self.on_head = None
        self.head = tk.Canvas(self, height=self.hh, bg=C['bg'], highlightthickness=0, bd=0)
        self.body = tk.Canvas(self, bg=C['panel'], highlightthickness=0, bd=0, takefocus=1,
                              yscrollincrement=self.rh)
        self.vsb = ttk.Scrollbar(self, orient='vertical', command=self.body.yview)
        self.body.configure(yscrollcommand=self.vsb.set)
        self.head.grid(row=0, column=0, sticky='ew')
        self.body.grid(row=1, column=0, sticky='nsew')
        self.vsb.grid(row=0, column=1, rowspan=2, sticky='ns')
        self.columnconfigure(0, weight=1)
        self.rowconfigure(1, weight=1)
        self.body.bind('<Configure>', lambda e: self.redraw())
        self.body.bind('<Button-1>', self._click)
        self.body.bind('<Double-1>', self._double)
        self.body.bind('<Button-3>', self._menu)
        self.head.bind('<Button-1>', self._head_click)
        for w in (self.body, self.head):
            w.bind('<MouseWheel>', lambda e: self.body.yview_scroll(int(-e.delta / 40), 'units'))

    def layout(self):
        s = self.app.scale
        W = max(self.body.winfo_width(), 300)
        fixed = sum(int(c['w'] * s) for c in self.cols if not c.get('flex'))
        out, x = [], 0
        for c in self.cols:
            w = int(c['w'] * s) if not c.get('flex') else max(int(c['w'] * s), W - fixed)
            out.append((c, x, w))
            x += w
        return out, max(W, x)

    def set_rows(self, rows):
        self.rows = rows
        ids = {r['id'] for r in rows}
        self.sel &= ids
        self.redraw()

    def selection(self):
        return [r['id'] for r in self.rows if r['id'] in self.sel]

    def select(self, ids):
        self.sel = set(ids)
        self.redraw()

    def see(self, rid):
        ids = [r['id'] for r in self.rows]
        if rid not in ids or not ids:
            return
        i = ids.index(rid)
        top, bottom = self.body.yview()
        total = len(ids) * self.rh
        y = i * self.rh
        if y < top * total or y + self.rh > bottom * total:
            self.body.yview_moveto(max(0.0, (y - 2 * self.rh) / total))

    def redraw(self):
        lay, W = self.layout()
        hc, bc = self.head, self.body
        hc.delete('all')
        hc.create_rectangle(0, 0, W, self.hh, fill=C['bg'], outline='')
        pad = int(10 * self.app.scale)
        for c, x, w in lay:
            if c.get('center') or c['kind'] in ('check', 'num'):
                hc.create_text(x + w / 2, self.hh / 2, text=c['title'], fill=C['muted'], font=self.head_font)
            else:
                hc.create_text(x + pad, self.hh / 2, text=fit(self.head_font, c['title'], w - pad),
                               anchor='w', fill=C['muted'], font=self.head_font)
        hc.create_line(0, self.hh - 1, W, self.hh - 1, fill=C['border'])

        bc.delete('cell')
        for i, row in enumerate(self.rows):
            y0 = i * self.rh
            bg = C['sel'] if row['id'] in self.sel else (C['stripe'] if i % 2 else C['panel'])
            bc.create_rectangle(0, y0, W, y0 + self.rh, fill=bg, outline='', tags='cell')
            for c, x, w in lay:
                self._cell(row, c, x, y0, w, bg)
        bc.configure(scrollregion=(0, 0, W, max(len(self.rows) * self.rh, 1)))
        bc.tag_raise('editor')

    def _cell(self, row, c, x, y0, w, bg):
        bc, s = self.body, self.app.scale
        key, kind = c['key'], c['kind']
        val = row.get(key, '')
        changed = key in row.get('changed', ())
        cy = y0 + self.rh / 2
        pad = int(10 * s)
        if kind == 'num':
            bc.create_text(x + w / 2, cy, text=val, fill=C['faint'], font=self.font, tags='cell')
        elif kind == 'check':
            bc.create_image(x + w / 2, cy, image=self.app.box('on' if val else 'off', bg), tags='cell')
        elif kind == 'pill':
            txt = fit(self.pill_font, str(val), w - 3 * pad)
            tw = self.pill_font.measure(txt) + int(16 * s)
            rrect(bc, x + pad, y0 + 7 * s, x + pad + tw, y0 + self.rh - 7 * s, 9 * s,
                  fill=C['pill_bg'], outline=C['edit'] if changed else '', tags='cell')
            bc.create_text(x + pad + tw / 2, cy, text=txt, fill=C['edit'] if changed else C['pill_fg'],
                           font=self.pill_font, tags='cell')
        elif kind == 'field':
            x1, x2 = x + int(5 * s), x + w - int(5 * s)
            rrect(bc, x1, y0 + 5 * s, x2, y0 + self.rh - 5 * s, 6 * s, fill=C['field'],
                  outline=C['edit'] if changed else C['border'], tags='cell')
            if c.get('browse'):
                bx1, bx2 = x2 - self.bw, x2 - int(3 * s)
                rrect(bc, bx1, y0 + 8 * s, bx2, y0 + self.rh - 8 * s, 4 * s, fill=C['hover'], outline='',
                      tags='cell')
                bc.create_text((bx1 + bx2) / 2, cy, text=self.icon, font=self.icon_font, fill=C['fg'],
                               tags='cell')
                x2 = bx1
            empty = not val
            txt = c.get('ph', '') if empty else str(val)
            txt = fit(self.font, txt, x2 - x1 - 2 * pad)
            col = C['faint'] if empty else (C['edit'] if changed else C['fg'])
            if c.get('center'):
                bc.create_text((x1 + x2) / 2, cy, text=txt, fill=col, font=self.font, tags='cell')
            else:
                bc.create_text(x1 + pad, cy, text=txt, anchor='w', fill=col, font=self.font, tags='cell')
        else:
            col = C['muted'] if kind == 'muted' else C['fg']
            if row.get('accent_key') == key:
                col = row.get('accent', col)
            bc.create_text(x + pad, cy, text=fit(self.font, str(val), w - 2 * pad), anchor='w',
                           fill=col, font=self.font, tags='cell')

    def _hit(self, e):
        i = int(self.body.canvasy(e.y) // self.rh)
        if not 0 <= i < len(self.rows):
            return None, None
        lay, _ = self.layout()
        for c, x, w in lay:
            if x <= e.x < x + w:
                return i, c
        return i, None

    def _on_browse_btn(self, e, c):
        if not c or not c.get('browse'):
            return False
        lay, _ = self.layout()
        x, w = next((x, w) for cc, x, w in lay if cc is c)
        return e.x >= x + w - int(5 * self.app.scale) - self.bw

    def _click(self, e):
        self.body.focus_set()
        i, c = self._hit(e)
        if i is None:
            return
        rid = self.rows[i]['id']
        if c and c['kind'] == 'check' and self.on_toggle:
            self.on_toggle(rid, c['key'])
            return
        if self._on_browse_btn(e, c) and self.on_browse:
            self.on_browse(rid, c['key'])
            return
        ids = [r['id'] for r in self.rows]
        if e.state & 0x1 and self.anchor in ids:          # shift
            a, b = sorted((ids.index(self.anchor), i))
            self.sel = set(ids[a:b + 1])
        elif e.state & 0x4:                                # ctrl
            self.sel ^= {rid}
            self.anchor = rid
        else:
            self.sel = {rid}
            self.anchor = rid
        self.redraw()

    def _head_click(self, e):
        lay, _ = self.layout()
        x = self.head.canvasx(e.x)
        for c, cx, w in lay:
            if cx <= x < cx + w and self.on_head:
                self.on_head(c['key'])
                return

    def _double(self, e):
        i, c = self._hit(e)
        if i is None or not c or self._on_browse_btn(e, c):
            return
        rid = self.rows[i]['id']
        if c.get('edit') and self.on_edit:
            self.on_edit(rid, c['key'])
        elif c.get('open') and self.on_open:
            self.on_open(rid)

    def _menu(self, e):
        i, _ = self._hit(e)
        if i is None:
            return
        rid = self.rows[i]['id']
        if rid not in self.sel:
            self.sel = {rid}
            self.anchor = rid
            self.redraw()
        if self.on_menu:
            self.on_menu(e)

    def edit_entry(self, rid, key, value, on_commit):
        ids = [r['id'] for r in self.rows]
        if rid not in ids:
            return
        i = ids.index(rid)
        lay, _ = self.layout()
        x, w = next((x, w) for c, x, w in lay if c['key'] == key)
        s = self.app.scale
        self.body.delete('editor')
        e = tk.Entry(self.body, bg=C['field'], fg=C['fg'], insertbackground=C['fg'], relief='flat', bd=0,
                     highlightthickness=1, highlightbackground=C['accent'], highlightcolor=C['accent'],
                     font=self.app.f_base, selectbackground=C['sel'], selectforeground=C['fg'])
        e.insert(0, value)
        e.select_range(0, 'end')
        self.body.create_window(x + int(5 * s), i * self.rh + int(5 * s), anchor='nw', window=e,
                                width=max(w - int(10 * s), int(200 * s)), height=self.rh - int(10 * s),
                                tags='editor')
        e.focus_set()
        done = [False]

        def finish(commit):
            if done[0]:
                return
            done[0] = True
            v = e.get()
            self.body.delete('editor')
            e.destroy()
            self.body.focus_set()
            if commit:
                on_commit(v)

        e.bind('<Return>', lambda _: finish(True))
        e.bind('<KP_Enter>', lambda _: finish(True))
        e.bind('<FocusOut>', lambda _: finish(True))
        e.bind('<Escape>', lambda _: finish(False))


class Dialog(tk.Toplevel):
    """Dark modal-ish window with a bordered body card."""

    def __init__(self, app, title, w=720, h=480):
        super().__init__(app.root)
        self.app = app
        self.title(title)
        self.configure(bg=C['bg'])
        self.transient(app.root)
        s = app.scale
        # never bigger than the screen; the body scrolls instead and the buttons stay visible
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        W, H = min(int(w * s), sw - 40), min(int(h * s), sh - int(90 * s))
        x = min(max(app.root.winfo_rootx() + int(80 * s), 0), max(sw - W - 10, 0))
        y = min(max(app.root.winfo_rooty() + int(60 * s), 0), max(sh - H - int(60 * s), 0))
        self.geometry(f'{W}x{H}+{x}+{y}')
        self.minsize(min(int(420 * s), W), min(int(260 * s), H))
        outer = ttk.Frame(self, padding=14)
        outer.pack(fill='both', expand=True)
        ttk.Label(outer, text=title, style='Title.TLabel').pack(anchor='w', pady=(0, 10))
        self.buttons = ttk.Frame(outer, padding=(0, 10, 0, 0))
        self.buttons.pack(side='bottom', fill='x')          # packed first so it is never squeezed out
        box = card(outer)
        box.pack(fill='both', expand=True)
        self.scroll = ScrollFrame(box, C['panel'])
        self.scroll.pack(fill='both', expand=True)
        self.body = ttk.Frame(self.scroll.inner, style='Card.TFrame', padding=14)
        self.body.pack(fill='both', expand=True)
        dark_titlebar(self)


# ---------------------------------------------------------------- app
class App:
    T2_COLS = [
        dict(key='n', title='#', w=38, kind='num'),
        dict(key='sync', title='SYNC', w=54, kind='check'),
        dict(key='lang', title='LANG CODE', w=100, kind='pill', edit=True),
        dict(key='lname', title='LANGUAGE', w=150, kind='muted', open=True),
        dict(key='name', title='TRACK NAME', w=180, kind='field', edit=True, ph='no name'),
        dict(key='sdh', title='SDH', w=52, kind='check'),
        dict(key='forced', title='FORCED', w=64, kind='check'),
        dict(key='default', title='DEFAULT', w=68, kind='check'),
        dict(key='delay', title='DELAY', w=90, kind='field', edit=True, ph='0 ms', center=True),
        dict(key='tags', title='SOURCE TAG  (text or .xml)', w=250, kind='field', edit=True,
             ph='+ add source', browse=True),
        dict(key='provider', title='PROVIDER', w=92, kind='muted', open=True),
        dict(key='file', title='FILE', w=140, kind='muted', flex=True, open=True),
    ]

    def __init__(self, root, folder):
        self.root = root
        root.title('SubPicker')
        self.scale = root.winfo_fpixels('1i') / 96
        sw, sh = root.winfo_screenwidth(), root.winfo_screenheight()
        root.geometry(f'{min(int(1300 * self.scale), sw - 20)}x{min(int(820 * self.scale), sh - int(80 * self.scale))}')
        root.minsize(min(int(1100 * self.scale), sw - 20), min(int(480 * self.scale), sh - int(80 * self.scale)))
        root.configure(bg=C['bg'])
        self.cfg = load_settings()
        PROVIDERS.update({k.lower(): v for k, v in self.cfg.get('provider_names', {}).items()})
        self.learned = {}
        self.folder = ''
        self.title = ''
        self.subs = []
        self.sel = {}
        self.prov_order = []
        self.prov_on = {}
        self.overrides = {}
        self.no_sync = set()            # track paths unticked in the SYNC column
        self.manual_order = None
        self.boxes = {}
        self.page = tk.StringVar(value='pick')
        self.view = tk.StringVar(value='all')
        self.sort_mode = tk.StringVar(value='missing')
        self.numbered = tk.BooleanVar(value=False)
        self.out_fmt = tk.StringVar(value='mks')
        self.out_path = tk.StringVar()
        self.status = tk.StringVar()
        self.order_info = tk.StringVar()
        self.dl_query = tk.StringVar()
        self.dl_dir = tk.StringVar(value=self.cfg.get('download_dir', ''))
        self.dl_open = tk.BooleanVar(value=True)
        self.dl_info = tk.StringVar(value='Enter a TMDB / IMDb id (e.g. 1698863 or tt1234567) or a title.')
        self.dl_groups = {}
        self.dl_sel = {}
        self.ui_queue = queue.Queue()
        self._pump()
        self._fonts()
        self._style()
        self._build()
        dark_titlebar(root)
        enable_file_drop(root, lambda paths: self.post(self.on_drop, paths))
        if folder:
            self.load(folder)
        else:
            self.page.set('download')
            self.show_page()

    # ---------- thread-safe UI calls ----------
    def post(self, fn, *args):
        """Run fn(*args) on the Tk thread (safe to call from worker threads)."""
        self.ui_queue.put((fn, args))

    def _pump(self):
        try:
            while True:
                fn, args = self.ui_queue.get_nowait()
                try:
                    fn(*args)
                except Exception as ex:          # keep the UI alive
                    print('UI callback error:', ex, file=sys.stderr)
        except queue.Empty:
            pass
        self.root.after(40, self._pump)

    # ---------- look ----------
    def _fonts(self):
        fams = set(tkfont.families(self.root))
        fam = next((f for f in ('Segoe UI Variable Text', 'Segoe UI', 'Inter', 'Helvetica')
                    if f in fams), 'TkDefaultFont')
        semi = next((f for f in ('Segoe UI Variable Text Semibold', 'Segoe UI Semibold') if f in fams), None)
        disp = next((f for f in ('Segoe UI Variable Display Semib', 'Segoe UI Variable Display',
                                 'Segoe UI Semibold') if f in fams), None)
        mono = next((f for f in ('Cascadia Mono', 'Consolas') if f in fams), 'TkFixedFont')
        self.f_base = (fam, 9)
        self.f_small = (fam, 8)
        self.f_bold = (semi, 9) if semi else (fam, 9, 'bold')
        self.f_small_bold = (semi, 8) if semi else (fam, 8, 'bold')
        self.f_title = (disp, 15) if disp else (fam, 15, 'bold')
        self.f_mono = (mono, 9)
        for n in ('TkDefaultFont', 'TkTextFont', 'TkMenuFont', 'TkHeadingFont', 'TkCaptionFont'):
            try:
                tkfont.nametofont(n).configure(family=fam, size=9)
            except tk.TclError:
                pass

    def _style(self):
        st = ttk.Style(self.root)
        st.theme_use('clam')
        sc = self.scale
        st.configure('.', background=C['bg'], foreground=C['fg'], fieldbackground=C['field'],
                     bordercolor=C['border'], darkcolor=C['bg'], lightcolor=C['bg'],
                     troughcolor=C['panel'], focuscolor=C['accent'], selectbackground=C['sel'],
                     selectforeground=C['fg'], insertcolor=C['fg'], font=self.f_base, relief='flat')
        st.map('.', foreground=[('disabled', C['faint'])])
        st.configure('TFrame', background=C['bg'])
        st.configure('Card.TFrame', background=C['panel'])
        st.configure('TLabel', background=C['bg'], foreground=C['fg'])
        st.configure('Muted.TLabel', foreground=C['muted'], font=self.f_small)
        st.configure('Title.TLabel', font=self.f_title)
        st.configure('Card.TLabel', background=C['panel'])
        st.configure('CardHead.TLabel', background=C['panel'], foreground=C['muted'], font=self.f_small_bold)
        st.configure('CardMuted.TLabel', background=C['panel'], foreground=C['muted'], font=self.f_small)
        st.configure('Badge.TLabel', background=C['panel'], foreground=C['accent'], font=self.f_small_bold)
        st.configure('Count.TLabel', background=C['panel'], foreground=C['faint'], font=self.f_small)

        st.configure('TButton', background=C['field'], foreground=C['fg'], bordercolor=C['border'],
                     lightcolor=C['field'], darkcolor=C['field'], padding=(10, 4), focusthickness=0)
        st.map('TButton', background=[('pressed', C['border']), ('active', C['hover'])],
               lightcolor=[('active', C['hover'])], darkcolor=[('active', C['hover'])])
        st.configure('Accent.TButton', background=C['accent'], foreground='#ffffff',
                     bordercolor=C['accent'], lightcolor=C['accent'], darkcolor=C['accent'],
                     font=self.f_bold, padding=(16, 6))
        st.map('Accent.TButton',
               background=[('disabled', C['field']), ('pressed', C['accent_dn']), ('active', C['accent_hi'])],
               lightcolor=[('disabled', C['field']), ('active', C['accent_hi'])],
               darkcolor=[('disabled', C['field']), ('active', C['accent_hi'])],
               bordercolor=[('disabled', C['border'])], foreground=[('disabled', C['muted'])])
        st.configure('Tiny.TButton', padding=(4, 0), font=self.f_small)

        for base in ('TCheckbutton', 'TRadiobutton'):
            for pre, bg in (('', C['bg']), ('Card.', C['panel'])):
                st.configure(pre + base, background=bg, foreground=C['fg'], indicatorbackground=C['field'],
                             indicatorforeground='#ffffff', upperbordercolor=C['box_border'],
                             lowerbordercolor=C['box_border'], indicatormargin=(0, 0, 6, 0),
                             indicatorsize=int(12 * sc), padding=(0, 2), focusthickness=0)
                st.map(pre + base, background=[('active', bg)],
                       indicatorbackground=[('selected', C['accent']), ('active', C['hover'])],
                       upperbordercolor=[('selected', C['accent'])],
                       lowerbordercolor=[('selected', C['accent'])],
                       foreground=[('disabled', C['faint'])])

        st.configure('Treeview', background=C['panel'], fieldbackground=C['panel'], foreground=C['fg'],
                     bordercolor=C['panel'], lightcolor=C['panel'], darkcolor=C['panel'],
                     rowheight=int(26 * sc), borderwidth=0, font=self.f_base)
        st.map('Treeview', background=[('selected', C['sel'])], foreground=[('selected', C['fg'])])
        st.configure('Treeview.Heading', background=C['bg'], foreground=C['muted'], bordercolor=C['border'],
                     lightcolor=C['bg'], darkcolor=C['bg'], relief='flat', font=self.f_small_bold,
                     padding=(8, 6))
        st.map('Treeview.Heading', background=[('active', C['field'])])
        st.layout('Treeview', [('Treeview.treearea', {'sticky': 'nswe'})])

        st.configure('TEntry', fieldbackground=C['field'], foreground=C['fg'], bordercolor=C['border'],
                     lightcolor=C['field'], darkcolor=C['field'], insertcolor=C['fg'], padding=(8, 4))
        st.map('TEntry', bordercolor=[('focus', C['accent'])], lightcolor=[('focus', C['accent'])])
        st.configure('TCombobox', fieldbackground=C['field'], background=C['field'], foreground=C['fg'],
                     arrowcolor=C['muted'], bordercolor=C['border'], lightcolor=C['field'], darkcolor=C['field'],
                     selectbackground=C['field'], selectforeground=C['fg'], padding=(6, 4))
        st.map('TCombobox', fieldbackground=[('readonly', C['field'])], background=[('active', C['hover'])],
               bordercolor=[('focus', C['accent'])])
        self.root.option_add('*TCombobox*Listbox.background', C['field'])
        self.root.option_add('*TCombobox*Listbox.foreground', C['fg'])
        self.root.option_add('*TCombobox*Listbox.selectBackground', C['sel'])
        self.root.option_add('*TCombobox*Listbox.font', self.f_base)
        st.configure('Vertical.TScrollbar', background=C['field'], troughcolor=C['panel'],
                     bordercolor=C['panel'], lightcolor=C['field'], darkcolor=C['field'],
                     arrowcolor=C['muted'], gripcount=0, arrowsize=int(11 * sc))
        st.map('Vertical.TScrollbar', background=[('active', C['hover'])])
        st.configure('Horizontal.TProgressbar', background=C['accent'], troughcolor=C['field'],
                     bordercolor=C['field'], lightcolor=C['accent'], darkcolor=C['accent'],
                     thickness=int(6 * sc))

    def box(self, state, bg):
        key = (state, bg)
        if key not in self.boxes:
            self.boxes[key] = make_box(state, bg, int(16 * self.scale))
        return self.boxes[key]

    def check(self, parent, text, var, bg, command=None):
        """Checkbutton that uses the drawn tick boxes."""
        style = 'ImgCard.TCheckbutton' if bg == C['panel'] else 'Img.TCheckbutton'
        st = ttk.Style(self.root)
        st.layout(style, [('Checkbutton.padding', {'sticky': 'nswe', 'children': [
            ('Checkbutton.label', {'sticky': 'nswe'})]})])
        st.configure(style, background=bg, foreground=C['fg'], padding=(0, 2), focusthickness=0)
        st.map(style, background=[('active', bg)], foreground=[('disabled', C['faint'])])
        off, on = self.box('off', bg), self.box('on', bg)
        return ttk.Checkbutton(parent, text='  ' + text, variable=var, style=style, compound='left',
                               image=(off, 'selected', on, 'disabled', off), command=command)

    # ---------- layout ----------
    def _build(self):
        hdr = ttk.Frame(self.root, padding=(16, 12, 16, 8))
        hdr.pack(fill='x')
        ttk.Label(hdr, text='SubPicker', style='Title.TLabel').pack(side='left')
        self.tabs = Segmented(hdr, self, [('download', 'Download'), ('pick', 'Pick subtitles'),
                                          ('order', 'Track order')], self.page, command=self.show_page)
        self.tabs.pack(side='left', padx=(22, 0))
        ttk.Button(hdr, text='⚙  Settings', command=self.open_settings).pack(side='right')
        ttk.Button(hdr, text='Open folder…', command=self.pick_folder).pack(side='right', padx=(0, 6))
        ttk.Button(hdr, text='From MKV…', command=self.extract_from_mkv).pack(side='right', padx=(0, 6))
        self.folder_lbl = ttk.Label(hdr, style='Muted.TLabel')
        self.folder_lbl.pack(side='right', padx=12)

        ttk.Label(self.root, textvariable=self.status, style='Muted.TLabel',
                  padding=(18, 2, 18, 8)).pack(side='bottom', fill='x')
        self._build_export(self.root)

        self.main_scroll = ScrollFrame(self.root, C['bg'])   # scrolls when the window is too small
        self.main_scroll.pack(fill='both', expand=True)
        content = ttk.Frame(self.main_scroll.inner, padding=(14, 0, 14, 0))
        content.pack(fill='both', expand=True)
        content.columnconfigure(0, weight=1)
        content.rowconfigure(0, weight=1)
        self.page0 = ttk.Frame(content)
        self.page1 = ttk.Frame(content)
        self.page2 = ttk.Frame(content)
        for p in (self.page0, self.page1, self.page2):
            p.grid(row=0, column=0, sticky='nsew')
        self._build_download(self.page0)
        self._build_pick(self.page1)
        self._build_order(self.page2)
        self.show_page()

    def show_page(self):
        {'download': self.page0, 'pick': self.page1, 'order': self.page2}[self.page.get()].tkraise()

    def _card_head(self, parent, text, pady=(0, 4)):
        ttk.Label(parent, text=text.upper(), style='CardHead.TLabel').pack(anchor='w', pady=pady)

    def _build_export(self, parent):
        wrap = ttk.Frame(parent, padding=(14, 10, 14, 6))
        wrap.pack(side='bottom', fill='x')
        box = card(wrap)
        box.pack(fill='x')
        bar = ttk.Frame(box, style='Card.TFrame', padding=(12, 10))
        bar.pack(fill='x')
        ttk.Label(bar, text='EXPORT', style='CardHead.TLabel').pack(side='left', padx=(0, 12))
        self.fmt_seg = Segmented(bar, self, [('mks', 'MKS'), ('mkv', 'MKV'), ('srt', 'SRT files')],
                                 self.out_fmt, command=self.on_fmt, bg=C['panel'])
        self.fmt_seg.pack(side='left')
        ttk.Entry(bar, textvariable=self.out_path).pack(side='left', padx=(12, 6), fill='x', expand=True)
        ttk.Button(bar, text='Browse…', command=self.pick_out).pack(side='left')
        self.num_chk = self.check(bar, 'Number in track order', self.numbered, C['panel'])
        self.num_chk.pack(side='left', padx=(12, 0))
        self.export_btn = ttk.Button(bar, text='Export', style='Accent.TButton', command=self.export)
        self.export_btn.pack(side='left', padx=(12, 0))

    # ----- page 0: download -----
    def _build_download(self, t):
        t.columnconfigure(0, weight=1)
        t.rowconfigure(1, weight=1)
        sbox = card(t)
        sbox.grid(row=0, column=0, sticky='ew')
        top = ttk.Frame(sbox, style='Card.TFrame', padding=(14, 12))
        top.pack(fill='x')
        ttk.Label(top, text='SEARCH SUBTITLE SITE', style='CardHead.TLabel').pack(anchor='w')
        row = ttk.Frame(top, style='Card.TFrame')
        row.pack(fill='x', pady=(6, 0))
        e = ttk.Entry(row, textvariable=self.dl_query, width=40, font=self.f_bold)
        e.pack(side='left')
        e.bind('<Return>', lambda _: self.dl_search())
        self.dl_search_btn = ttk.Button(row, text='Search', style='Accent.TButton', command=self.dl_search)
        self.dl_search_btn.pack(side='left', padx=(8, 0))
        ttk.Label(row, textvariable=self.dl_info, style='CardMuted.TLabel').pack(side='left', padx=14)

        rbox = card(t)
        rbox.grid(row=1, column=0, sticky='nsew', pady=(10, 0))
        main = ttk.Frame(rbox, style='Card.TFrame')
        main.pack(fill='both', expand=True)
        main.columnconfigure(0, weight=1)
        main.rowconfigure(0, weight=1)
        cols = ('files', 'size', 'where')
        self.dl_tree = ttk.Treeview(main, columns=cols, selectmode='browse')
        self.dl_tree.heading('#0', text='FOLDER ON THE SITE', anchor='w')
        for c, h, w in (('files', 'FILES', 80), ('size', 'SIZE', 90), ('where', 'LIBRARY / PATH', 520)):
            self.dl_tree.heading(c, text=h, anchor='w' if c == 'where' else 'center')
            self.dl_tree.column(c, width=int(w * self.scale), anchor='w' if c == 'where' else 'center',
                                stretch=(c == 'where'))
        self.dl_tree.column('#0', width=int(420 * self.scale))
        vsb = ttk.Scrollbar(main, orient='vertical', command=self.dl_tree.yview)
        self.dl_tree.configure(yscrollcommand=vsb.set)
        self.dl_tree.grid(row=0, column=0, sticky='nsew')
        vsb.grid(row=0, column=1, sticky='ns')
        self.dl_tree.tag_configure('grp', font=self.f_bold)
        self.dl_tree.tag_configure('sub', foreground=C['muted'])
        self.dl_tree.bind('<Button-1>', self.dl_click)

        bbox = card(t)
        bbox.grid(row=2, column=0, sticky='ew', pady=(10, 0))
        bar = ttk.Frame(bbox, style='Card.TFrame', padding=(14, 10))
        bar.pack(fill='x')
        ttk.Label(bar, text='DOWNLOAD TO', style='CardHead.TLabel').pack(side='left', padx=(0, 10))
        ttk.Entry(bar, textvariable=self.dl_dir).pack(side='left', fill='x', expand=True)
        ttk.Button(bar, text='Browse…', command=self.dl_pick_dir).pack(side='left', padx=(6, 0))
        self.check(bar, 'Open in Pick subtitles when done', self.dl_open, C['panel']).pack(side='left', padx=12)
        self.dl_btn = ttk.Button(bar, text='Download & extract', style='Accent.TButton', command=self.dl_start)
        self.dl_btn.pack(side='left')
        prog = ttk.Frame(bbox, style='Card.TFrame', padding=(14, 0, 14, 10))
        prog.pack(fill='x')
        self.dl_prog = ttk.Progressbar(prog, maximum=100)
        self.dl_prog.pack(fill='x')
        self.dl_log = log_text(bbox, height=5, font=self.f_mono)
        self.dl_log.pack(fill='x', padx=1, pady=(0, 1))

    # ----- page 1: pick -----
    def _build_pick(self, t):
        t.columnconfigure(1, weight=1)
        t.rowconfigure(0, weight=1)
        sbox = card(t)
        sbox.grid(row=0, column=0, sticky='ns', padx=(0, 10))
        self.side_scroll = ScrollFrame(sbox, C['panel'], fit_width=True)   # scrolls on short windows
        self.side_scroll.pack(fill='both', expand=True)
        side = ttk.Frame(self.side_scroll.inner, style='Card.TFrame', padding=14)
        side.pack(fill='both', expand=True)
        self._card_head(side, 'Providers · priority order')
        ttk.Label(side, text='Drag to reorder · double-click = only its files', style='CardMuted.TLabel').pack(anchor='w')
        self.prov_frame = ttk.Frame(side, style='Card.TFrame')
        self.prov_frame.pack(fill='x', pady=(6, 12))

        self._card_head(side, 'Show')
        for val, txt in (('all', 'All languages'), ('missing', 'Missing from primary'),
                         ('primary', 'Only in primary')):
            ttk.Radiobutton(side, text=txt, value=val, variable=self.view, style='Card.TRadiobutton',
                            command=self.rebuild).pack(anchor='w')
        self._card_head(side, 'List order', pady=(12, 4))
        for val, txt in (('missing', 'Missing first'), ('mkv', 'MKV order (English, then A–Z)')):
            ttk.Radiobutton(side, text=txt, value=val, variable=self.sort_mode, style='Card.TRadiobutton',
                            command=self.rebuild).pack(anchor='w')
        self._card_head(side, 'Selection', pady=(12, 4))
        ttk.Button(side, text='Auto-select by priority', command=self.auto_select).pack(fill='x', pady=2)
        ttk.Button(side, text='Select none', command=self.select_none).pack(fill='x', pady=2)
        row = ttk.Frame(side, style='Card.TFrame')
        row.pack(fill='x', pady=2)
        ttk.Button(row, text='Expand', command=lambda: self.expand(True)).pack(side='left', fill='x', expand=True)
        ttk.Button(row, text='Collapse', command=lambda: self.expand(False)).pack(side='left', fill='x',
                                                                                   expand=True, padx=(4, 0))
        self._card_head(side, 'Add subtitles', pady=(12, 4))
        ttk.Button(side, text='Add subtitle files…', command=self.add_files).pack(fill='x', pady=2)
        ttk.Button(side, text='From MKV…', command=self.extract_from_mkv).pack(fill='x', pady=2)
        ttk.Label(side, style='CardMuted.TLabel', justify='left', wraplength=int(210 * self.scale), text=(
            'or drag & drop subtitle files, folders or an MKV onto the window.\n'
            '\nClick a file to tick it · click the box on a language row to tick all.\n'
            'Double-click a file to open it. Orange = missing from the primary.\n'
            '"auto" = detected from the subtitle text.')).pack(anchor='w')

        mbox = card(t)
        mbox.grid(row=0, column=1, sticky='nsew')
        main = ttk.Frame(mbox, style='Card.TFrame')
        main.pack(fill='both', expand=True)
        main.columnconfigure(0, weight=1)
        main.rowconfigure(0, weight=1)
        cols = ('provider', 'variant', 'cues', 'size', 'file')
        self.tree = ttk.Treeview(main, columns=cols, selectmode='extended')
        self.tree.heading('#0', text='LANGUAGE', anchor='w')
        for c, h in zip(cols, ('PROVIDER', 'VARIANT', 'CUES', 'SIZE', 'FILE / AVAILABLE FROM')):
            self.tree.heading(c, text=h, anchor='center' if c in ('cues', 'size') else 'w')
        widths = {'#0': 220, 'provider': 130, 'variant': 150, 'cues': 60, 'size': 70, 'file': 440}
        for c, w in widths.items():
            self.tree.column(c, width=int(w * self.scale), stretch=(c == 'file'), minwidth=40,
                             anchor='center' if c in ('cues', 'size') else 'w')
        vsb = ttk.Scrollbar(main, orient='vertical', command=self.tree.yview)
        self.tree.configure(yscrollcommand=vsb.set)
        self.tree.grid(row=0, column=0, sticky='nsew')
        vsb.grid(row=0, column=1, sticky='ns')
        self.tree.tag_configure('missing', background=C['miss_bg'], foreground=C['miss_fg'], font=self.f_bold)
        self.tree.tag_configure('have', background=C['have_bg'], foreground=C['have_fg'], font=self.f_bold)
        self.tree.tag_configure('on', foreground=C['fg'])
        self.tree.tag_configure('off', foreground=C['off_fg'])
        self.tree.bind('<Button-1>', self.on_click)
        self.tree.bind('<space>', self.on_space)
        self.tree.bind('<Double-1>', self.on_double)

    # ----- page 2: track order -----
    def _build_order(self, t):
        t.columnconfigure(0, weight=1)
        t.rowconfigure(1, weight=1)
        tb = ttk.Frame(t, padding=(0, 0, 0, 8))
        tb.grid(row=0, column=0, sticky='ew')
        ttk.Button(tb, text='Auto sort', command=self.auto_sort).pack(side='left')
        ttk.Button(tb, text='▲  Move up', command=lambda: self.move_track(-1)).pack(side='left', padx=(6, 0))
        ttk.Button(tb, text='▼  Move down', command=lambda: self.move_track(1)).pack(side='left', padx=(6, 0))
        ttk.Button(tb, text='Reset edits', command=self.reset_overrides).pack(side='left', padx=(6, 0))
        ttk.Button(tb, text='Sync…', style='Accent.TButton', command=self.sync_dialog).pack(side='left',
                                                                                             padx=(14, 0))
        ttk.Label(tb, textvariable=self.order_info, style='Muted.TLabel').pack(side='left', padx=14)
        ttk.Label(tb, style='Muted.TLabel',
                  text='SYNC = tracks the Sync button uses (click its header for all / none) · '
                       'double-click a field to edit · right-click for bulk actions'
                  ).pack(side='right')

        tbox = card(t)
        tbox.grid(row=1, column=0, sticky='nsew')
        self.table = Table(tbox, self, self.T2_COLS)
        self.table.pack(fill='both', expand=True)
        self.table.on_toggle = self.toggle_flag
        self.table.on_head = self.on_table_head
        self.table.on_edit = self.edit_cell
        self.table.on_open = lambda rid: self.open_file(self.track(rid[2:])['src'])
        self.table.on_menu = lambda e: self.menu2.tk_popup(e.x_root, e.y_root)
        self.table.on_browse = lambda rid, key: self.choose_tags(
            self.table.selection() if rid in self.table.sel else [rid])

        self.menu2 = tk.Menu(self.root, tearoff=0, bg=C['field'], fg=C['fg'], activebackground=C['sel'],
                             activeforeground=C['fg'], bd=0, font=self.f_base)
        sel = self.table.selection
        self.menu2.add_command(label='Sync ticked tracks…', command=self.sync_dialog)
        self.menu2.add_command(label='Tick SYNC only for selected tracks',
                               command=lambda: self.set_sync([r[2:] for r in sel()]))
        self.menu2.add_command(label='Tick SYNC for all tracks', command=lambda: self.set_sync(None))
        self.menu2.add_command(label='Undo sync', command=lambda: self.undo_sync(sel()))
        self.menu2.add_separator()
        self.menu2.add_command(label='Type Source text for selected tracks…',
                               command=lambda: self.ask_source_text(sel()))
        self.menu2.add_command(label='Choose existing tags XML for selected tracks…',
                               command=lambda: self.choose_tags(sel()))
        self.menu2.add_command(label='Use provider as Source  (e.g. "iTunes WEB-DL")',
                               command=lambda: self.source_from_provider(sel()))
        self.menu2.add_command(label='Clear Source tag', command=lambda: self.clear_tags(sel()))
        self.menu2.add_separator()
        self.menu2.add_command(label='Set delay for selected tracks…', command=lambda: self.ask_delay(sel()))
        self.menu2.add_command(label='Reset edits of selected tracks', command=lambda: self.reset_rows(sel()))

        ttk.Label(t, style='Muted.TLabel', padding=(2, 8, 0, 0), text=(
            'Everything here goes into the export · yellow = edited · ⟳ teal = synced copy is used · '
            'Source tag: type text (an XML is made for you) or a path to an existing .xml'
        )).grid(row=2, column=0, sticky='w')

    def build_providers(self):
        for w in self.prov_frame.winfo_children():
            w.destroy()
        counts = {}
        for s in self.subs:
            counts[s.provider] = counts.get(s.provider, 0) + 1
        prim = self.primary()
        fluent = 'Segoe Fluent Icons' in tkfont.families(self.root)
        only = getattr(self, 'prov_filter', None)
        if only not in self.prov_order:
            only = self.prov_filter = None
        # "All files" = the normal view (every provider); double-click a provider = only its files
        rbg = C['panel']
        allrow = tk.Frame(self.prov_frame, bg=rbg, cursor='hand2')
        allrow.pack(fill='x', pady=(0, 3))
        tk.Frame(allrow, bg=C['accent'] if not only else rbg, width=3).pack(side='left', fill='y')
        alllbl = tk.Label(allrow, text=f' All files', anchor='w', padx=4, pady=2, bg=rbg,
                          fg=C['pill_fg'] if not only else C['fg'],
                          font=self.f_bold if not only else self.f_base, cursor='hand2')
        alllbl.pack(side='left')
        allcnt = tk.Label(allrow, text=str(len(self.subs)), bg=rbg, fg=C['muted'], font=self.f_small)
        allcnt.pack(side='left', padx=6)
        for w in (allrow, alllbl, allcnt):
            w.bind('<Button-1>', lambda e: self.set_prov_filter(None))
        self.prov_rows = []
        for i, p in enumerate(self.prov_order):
            active = p == only
            row = tk.Frame(self.prov_frame, bg=rbg)
            row.pack(fill='x', pady=1)
            tk.Frame(row, bg=C['accent'] if active else rbg, width=3).pack(side='left', fill='y')
            grip = tk.Label(row, text='' if fluent else '≡', bg=rbg, fg=C['muted'],
                            font=('Segoe Fluent Icons', 10) if fluent else self.f_small, padx=2)
            grip.pack(side='right', padx=(8, 2))
            self.check(row, '', self.prov_on[p], rbg, self.on_prov_change).pack(side='left', padx=(4, 0))
            name = tk.Label(row, text=p, bg=rbg, fg=C['pill_fg'] if active else C['fg'],
                            font=self.f_bold if active else self.f_base)
            name.pack(side='left')
            cnt = tk.Label(row, text=str(counts.get(p, 0)), bg=rbg, fg=C['muted'], font=self.f_small)
            cnt.pack(side='left', padx=6)
            if p == prim:
                ttk.Label(row, text='PRIMARY', style='Badge.TLabel').pack(side='left')
            for w in (row, name, cnt):
                w.bind('<Double-1>', lambda e, p=p: self.set_prov_filter(p))
            for w in (row, grip, name, cnt):                # drag anywhere except the tick box
                w.bind('<ButtonPress-1>', lambda e, i=i, g=grip: self._prov_drag_start(i, g))
                w.bind('<B1-Motion>', self._prov_drag_move)
                w.bind('<ButtonRelease-1>', self._prov_drag_end)
            grip.bind('<Enter>', lambda e, g=grip: g.config(fg=C['fg']))
            grip.bind('<Leave>', lambda e, g=grip: g.config(fg=C['muted']))
            self.prov_rows.append(row)
        self._drop_line = tk.Frame(self.prov_frame, bg=C['accent'], height=2)
        self._drag = None

    def set_prov_filter(self, prov):
        self.prov_filter = prov
        self.build_providers()
        self.rebuild()

    def _prov_target(self, y_root):
        """Insert position (0..n) for the pointer's screen y."""
        for n, row in enumerate(self.prov_rows):
            if y_root < row.winfo_rooty() + row.winfo_height() / 2:
                return n
        return len(self.prov_rows)

    PROV_HOLD_MS = 250          # press and hold this long before a provider row can be dragged

    def _prov_drag_start(self, i, grip):
        self._drag = {'from': i, 'to': None, 'armed': False, 'grip': grip,
                      'timer': self.root.after(self.PROV_HOLD_MS, self._prov_drag_arm)}

    def _prov_drag_arm(self):
        d = self._drag
        if d:
            d['armed'] = True
            d['grip'].config(fg=C['accent'])
            self.root.config(cursor='sb_v_double_arrow')

    def _prov_drag_move(self, e):
        if not self._drag or not self._drag['armed']:
            return
        to = self._prov_target(e.y_root)
        self._drag['to'] = to
        rows = self.prov_rows
        y = (rows[to].winfo_y() - 1) if to < len(rows) else rows[-1].winfo_y() + rows[-1].winfo_height()
        self._drop_line.place(x=0, y=max(0, y), relwidth=1)
        self._drop_line.lift()

    def _prov_drag_end(self, e):
        d, self._drag = self._drag, None
        self._drop_line.place_forget()
        self.root.config(cursor='')
        if d:
            self.root.after_cancel(d['timer'])
            try:
                d['grip'].config(fg=C['muted'])
            except tk.TclError:
                pass
        if not d or not d['armed'] or d['to'] is None:
            return
        i, to = d['from'], d['to']
        if to in (i, i + 1):                                # dropped where it already is
            return
        o = self.prov_order
        item = o.pop(i)
        o.insert(to - 1 if to > i else to, item)
        self.on_prov_change()

    def on_prov_change(self):
        self.build_providers()
        self.rebuild()

    # ---------- settings ----------
    def open_settings(self):
        d = Dialog(self, 'Settings', 780, 660)
        tab = tk.StringVar(value='general')
        holder = ttk.Frame(d.body, style='Card.TFrame')
        gen = ttk.Frame(holder, style='Card.TFrame')
        tagf = ttk.Frame(holder, style='Card.TFrame')
        for f in (gen, tagf):
            f.grid(row=0, column=0, sticky='nsew')
        holder.rowconfigure(0, weight=1)
        holder.columnconfigure(0, weight=1)
        Segmented(d.body, self, [('general', 'General'), ('tags', 'Source tags')], tab,
                  command=lambda: (gen if tab.get() == 'general' else tagf).tkraise(),
                  bg=C['panel']).pack(anchor='w', pady=(0, 12))
        holder.pack(fill='both', expand=True)
        gen.tkraise()
        b = gen
        b.columnconfigure(1, weight=1)
        vars_ = {}
        tag_table = dict(self.cfg.get('source_tags') or {})
        self._build_tags_settings(d, tagf, vars_, tag_table)
        rows = [
            ('TOOLS', None, None),
            ('mkvextract (MKVToolNix)', 'mkvextract', 'file'),
            ('batchsync.py', 'batchsync', 'file'),
            ('alass', 'alass', 'file'),
            ('ffsubsync', 'ffsubsync', 'file'),
            ('7-Zip (7z.exe)', 'sevenzip', 'file'),
            ('WinRAR (WinRAR.exe)', 'winrar', 'file'),
            ('SUBTITLE SITE', None, None),
            ('Site URL', 'site_url', None),
            ('Username', 'site_user', None),
            ('Password', 'site_pass', 'pw'),
            ('FOLDERS', None, None),
            ('Default download folder', 'download_dir', 'dir'),
        ]
        r = 0
        for label, key, kind in rows:
            if key is None:
                ttk.Label(b, text=label, style='CardHead.TLabel').grid(row=r, column=0, columnspan=3, sticky='w',
                                                                       pady=(12 if r else 0, 4))
                r += 1
                continue
            val = unprotect(self.cfg.get(key, '')) if kind == 'pw' else self.cfg.get(key, '')
            v = tk.StringVar(value=val)
            vars_[key] = v
            ttk.Label(b, text=label, style='Card.TLabel').grid(row=r, column=0, sticky='w', pady=3, padx=(0, 12))
            ttk.Entry(b, textvariable=v, show='•' if kind == 'pw' else '').grid(row=r, column=1, sticky='ew', pady=3)
            if kind in ('file', 'dir'):
                def browse(v=v, kind=kind, label=label):
                    f = (filedialog.askdirectory(parent=d, title=label) if kind == 'dir'
                         else filedialog.askopenfilename(parent=d, title=label))
                    if f:
                        v.set(os.path.normpath(f))
                ttk.Button(b, text='Browse…', command=browse).grid(row=r, column=2, padx=(6, 0), pady=3)
            if kind == 'file':
                ok = bool(resolve_tool(val))
                ttk.Label(b, text='✔' if ok else '✘', style='Card.TLabel',
                          foreground=C['ok'] if ok else C['err']).grid(row=r, column=3, padx=(8, 0))
            r += 1
        ttk.Label(b, style='CardMuted.TLabel', wraplength=int(680 * self.scale), justify='left', text=(
            '\nTools found on PATH or installed (MKVToolNix) are filled in automatically; mkvmerge is taken '
            'from the same folder as mkvextract, and Python is found on its own. '
            'The password is stored encrypted for your Windows account in ' + settings_path())
        ).grid(row=r, column=0, columnspan=4, sticky='w')

        def save():
            for k, v in vars_.items():
                self.cfg[k] = protect(v.get().strip()) if k == 'site_pass' else v.get().strip()
            ext = resolve_tool(self.cfg.get('mkvextract'))
            if ext:                                         # keep mkvmerge in step with mkvextract
                self.cfg['mkvmerge'] = find_mkv_tool('mkvmerge', near=ext) or self.cfg.get('mkvmerge', '')
            self.cfg['source_tags'] = dict(tag_table)
            save_settings(self.cfg)
            if not self.dl_dir.get():
                self.dl_dir.set(self.cfg.get('download_dir', ''))
            d.destroy()
            self.refresh_tracks()

        ttk.Button(d.buttons, text='Save', style='Accent.TButton', command=save).pack(side='right')
        ttk.Button(d.buttons, text='Cancel', command=d.destroy).pack(side='right', padx=(0, 6))
        ttk.Button(d.buttons, text='Auto-detect tools', command=lambda: [
            vars_[k].set(v) for k, v in default_settings().items()
            if k in vars_ and v and not k.startswith('site') and k != 'download_dir']).pack(side='left')

    def _build_tags_settings(self, d, f, vars_, table):
        """Settings > Source tags: provider code -> Source tags XML, used automatically in Track order."""
        f.columnconfigure(1, weight=1)
        f.rowconfigure(3, weight=1)
        ttk.Label(f, text='TAGS FOLDER', style='CardHead.TLabel').grid(row=0, column=0, columnspan=4,
                                                                    sticky='w', pady=(0, 4))
        tdir = tk.StringVar(value=self.cfg.get('tags_dir', ''))
        vars_['tags_dir'] = tdir
        ttk.Label(f, text='Folder', style='Card.TLabel').grid(row=1, column=0, sticky='w', padx=(0, 12))
        ttk.Entry(f, textvariable=tdir).grid(row=1, column=1, sticky='ew')

        def browse_dir():
            p = filedialog.askdirectory(parent=d, title='Folder with your Source tag XML files')
            if p:
                tdir.set(os.path.normpath(p))
                scan()
        ttk.Button(f, text='Browse…', command=browse_dir).grid(row=1, column=2, padx=(6, 0))
        ttk.Button(f, text='Scan', command=lambda: scan()).grid(row=1, column=3, padx=(6, 0))
        info = tk.StringVar(value=(
            'Files named "<code> Source Tag.xml" are added by themselves, e.g. "iT Source Tag.xml" or '
            '"MEGOGO Source Tag.xml". The code is the provider tag in the subtitle name '
            '(Movie.FLMN.WEB.es.srt → FLMN); the full name works too.'))
        ttk.Label(f, textvariable=info, style='CardMuted.TLabel', wraplength=int(700 * self.scale),
                  justify='left').grid(row=2, column=0, columnspan=4, sticky='w', pady=(6, 10))

        box = ttk.Frame(f, style='Card.TFrame')
        box.grid(row=3, column=0, columnspan=4, sticky='nsew')
        box.columnconfigure(0, weight=1)
        box.rowconfigure(0, weight=1)
        tv = ttk.Treeview(box, columns=('prov', 'xml'), selectmode='extended', height=9)
        tv.heading('#0', text='CODE', anchor='w')
        tv.heading('prov', text='PROVIDER', anchor='w')
        tv.heading('xml', text='SOURCE TAG XML', anchor='w')
        tv.column('#0', width=int(150 * self.scale), stretch=False)
        tv.column('prov', width=int(140 * self.scale), stretch=False)
        tv.column('xml', width=int(400 * self.scale))
        tv.tag_configure('missing', foreground=C['err'])
        vsb = ttk.Scrollbar(box, orient='vertical', command=tv.yview)
        tv.configure(yscrollcommand=vsb.set)
        tv.grid(row=0, column=0, sticky='nsew')
        vsb.grid(row=0, column=1, sticky='ns')

        def fill():
            tv.delete(*tv.get_children())
            for code in sorted(table, key=lambda c: provider_label(c).lower()):
                p = table[code]
                tv.insert('', 'end', iid=code, text='  ' + code, values=(provider_label(code), p),
                          tags=() if os.path.isfile(p) else ('missing',))

        def scan():
            folder = tdir.get().strip()
            if not os.path.isdir(folder):
                info.set('Folder not found.')
                return
            found, unknown = scan_tags_dir(folder)
            have = {k.lower() for k in table}
            new = {k: v for k, v in found.items() if k.lower() not in have}
            table.update(new)
            fill()
            msg = f'Scan: {len(new)} new, {len(found) - len(new)} already listed.'
            if unknown:
                msg += ('  Not matched to a provider (use "Add new" and type the code from the subtitle '
                        'name): ' + ', '.join(unknown))
            info.set(msg)

        def edit(code=None):
            e = Dialog(self, 'Change source tag' if code else 'Add source tag', 620, 330)
            e.transient(d)
            eb = e.body
            eb.columnconfigure(1, weight=1)
            cv = tk.StringVar(value=code or '')
            pv = tk.StringVar(value=table.get(code, '') if code else '')
            ttk.Label(eb, text='Provider code', style='Card.TLabel').grid(row=0, column=0, sticky='w',
                                                                         padx=(0, 12), pady=3)
            ce = ttk.Entry(eb, textvariable=cv, width=16)
            ce.grid(row=0, column=1, sticky='w', pady=3)
            name = tk.StringVar()
            ttk.Label(eb, textvariable=name, style='CardMuted.TLabel').grid(row=1, column=1, sticky='w')
            cv.trace_add('write', lambda *_: name.set(
                ('→ ' + provider_label(cv.get()) + ('' if known_provider(cv.get()) else '  (new provider)'))
                if cv.get().strip() else 'as written in the subtitle name, e.g. FLMN, iT, MEGOGO'))
            cv.set(cv.get())
            ttk.Label(eb, text='Source tag XML', style='Card.TLabel').grid(row=2, column=0, sticky='w',
                                                                          padx=(0, 12), pady=(10, 3))
            ttk.Entry(eb, textvariable=pv).grid(row=2, column=1, sticky='ew', pady=(10, 3))

            def pick():
                start = os.path.dirname(pv.get()) if pv.get() else tdir.get()
                p = filedialog.askopenfilename(parent=e, title='Source tag XML',
                                               initialdir=start if os.path.isdir(start) else None,
                                               filetypes=[('Matroska tags XML', '*.xml'), ('All', '*.*')])
                if p:
                    pv.set(os.path.normpath(p))
                    if not cv.get().strip():
                        m = TAG_FILE_RE.match(os.path.basename(p))
                        if m:
                            cv.set(m.group(1).strip())
            ttk.Button(eb, text='Browse…', command=pick).grid(row=2, column=2, padx=(6, 0), pady=(10, 3))

            def ok():
                c, p = cv.get().strip(), pv.get().strip().strip('"')
                if not c:
                    messagebox.showerror('SubPicker', 'Type the provider code.', parent=e)
                    return
                if not os.path.isfile(p):
                    messagebox.showerror('SubPicker', 'Choose an existing XML file.', parent=e)
                    return
                if code:
                    table.pop(code, None)
                for k in [k for k in table if k.lower() == c.lower()]:
                    table.pop(k)
                table[c] = os.path.normpath(p)     # matched against the code in subtitle names
                fill()
                tv.selection_set(c)
                tv.see(c)
                e.destroy()
            ttk.Button(e.buttons, text='OK', style='Accent.TButton', command=ok).pack(side='right')
            ttk.Button(e.buttons, text='Cancel', command=e.destroy).pack(side='right', padx=(0, 6))
            ce.focus_set()
            e.bind('<Return>', lambda _: ok())

        def remove():
            for c in tv.selection():
                table.pop(c, None)
            fill()

        bar = ttk.Frame(f, style='Card.TFrame')
        bar.grid(row=4, column=0, columnspan=4, sticky='ew', pady=(10, 0))
        ttk.Button(bar, text='+ Add new…', style='Accent.TButton', command=edit).pack(side='left')
        ttk.Button(bar, text='Change…', command=lambda: tv.selection() and edit(tv.selection()[0])
                   ).pack(side='left', padx=(6, 0))
        ttk.Button(bar, text='Remove', command=remove).pack(side='left', padx=(6, 0))
        ttk.Label(bar, style='CardMuted.TLabel', text='Double-click a row to change it.').pack(side='right')
        tv.bind('<Double-1>', lambda ev: tv.identify_row(ev.y) and edit(tv.identify_row(ev.y)))
        fill()

    # ---------- page 0: download ----------
    def site(self):
        url = self.cfg.get('site_url', '').strip()
        if not url:
            raise RuntimeError('Set the site URL in Settings.')
        return SiteClient(url, self.cfg.get('site_user', ''), unprotect(self.cfg.get('site_pass', '')))

    def dl_log_write(self, text, tag=None):
        self.post(text_write, self.dl_log, text, tag)

    def dl_search(self):
        text = self.dl_query.get().strip()
        if not text:
            return
        query, needle = build_query(text)
        self.dl_search_btn.state(['disabled'])
        self.dl_info.set('Searching…')

        def run():
            try:
                res = self.site().search(query)
                groups = group_hits(res.get('hits', []), needle)
                self.post(self.dl_show, groups, res.get('trunc'), needle)
            except Exception as ex:
                self.post(self.dl_info.set, f'Search failed: {ex}')
            finally:
                self.post(lambda: self.dl_search_btn.state(['!disabled']))

        threading.Thread(target=run, daemon=True).start()

    def dl_show(self, groups, trunc, needle):
        self.dl_groups = groups
        self.dl_sel = {k: True for k in groups}
        self.dl_tree.delete(*self.dl_tree.get_children())
        for root in sorted(groups):
            g = groups[root]
            name = root.rsplit('/', 1)[-1]
            iid = 'G:' + root
            self.dl_tree.insert('', 'end', iid=iid, text=f'  {name}', open=True, tags=('grp',),
                                image=self.box('on', C['panel']),
                                values=(g['files'], f"{g['size'] / 1024:.0f} KB", root))
            for sub, n in sorted(g['sub'].items()):
                self.dl_tree.insert(iid, 'end', text=f'    {sub}', values=(n, '', ''), tags=('sub',))
        nfiles = sum(g['files'] for g in groups.values())
        if not groups:
            self.dl_info.set(f'No folder with {needle} found.')
        else:
            self.dl_info.set(f'{len(groups)} folder(s), {nfiles} files' + ('  (results truncated)' if trunc else ''))
        if groups and not self.dl_dir.get():
            base = self.cfg.get('download_dir') or os.path.join(os.path.expanduser('~'), 'Downloads')
            self.dl_dir.set(base)

    def dl_click(self, e):
        iid = self.dl_tree.identify_row(e.y)
        if not iid.startswith('G:'):
            return
        try:
            elem = self.dl_tree.identify_element(e.x, e.y)
        except tk.TclError:
            elem = ''
        if 'image' in elem or self.dl_tree.identify_column(e.x) != '#0':
            root = iid[2:]
            self.dl_sel[root] = not self.dl_sel[root]
            self.dl_tree.item(iid, image=self.box('on' if self.dl_sel[root] else 'off', C['panel']))

    def dl_pick_dir(self):
        d = filedialog.askdirectory(initialdir=self.dl_dir.get() or os.path.expanduser('~'), title='Download to')
        if d:
            self.dl_dir.set(os.path.normpath(d))

    def dl_start(self):
        roots = [r for r, on in self.dl_sel.items() if on]
        if not roots:
            messagebox.showinfo('SubPicker', 'Search first and tick at least one folder.')
            return
        base = self.dl_dir.get().strip()
        if not base:
            messagebox.showwarning('SubPicker', 'Choose where to download.')
            return
        name = re.sub(r'[<>:"/\\|?*]', '_', roots[0].rsplit('/', 1)[-1]).strip() or 'subtitles'
        target = os.path.join(base, name)
        self.dl_btn.state(['disabled'])
        self.dl_prog['value'] = 0
        self.dl_log.config(state='normal')
        self.dl_log.delete('1.0', 'end')
        self.dl_log.config(state='disabled')
        threading.Thread(target=self._dl_run, args=(roots, target, self.dl_open.get()), daemon=True).start()

    def _dl_run(self, roots, target, open_after):
        log = self.dl_log_write
        added = 0
        self.learned = {}
        try:
            site = self.site()
            os.makedirs(target, exist_ok=True)
            tmp = tempfile.mkdtemp(prefix='subpicker_dl_')
            try:
                for n, root in enumerate(roots, 1):
                    name = root.rsplit('/', 1)[-1]
                    log(f'[{n}/{len(roots)}] Downloading {root}', 'head')
                    zpath = os.path.join(tmp, f'{n}.zip')
                    url = '/' + urllib.parse.quote(root, safe='/') + '/?zip'

                    def prog(got, total, n=n):
                        frac = (got / total) if total else 0.5
                        pct = ((n - 1) + frac) / len(roots) * 100
                        self.post(self.dl_prog.configure, {'value': pct})
                        self.post(self.dl_info.set, f'Downloading {name}: {got / 1048576:.1f} MB')

                    site.download(url, zpath, prog)
                    log(f'    zip {os.path.getsize(zpath) / 1024:.0f} KB, extracting…')
                    xdir = os.path.join(tmp, f'x{n}')
                    if not extract_archive(zpath, xdir, self.cfg, log):
                        log('    could not extract the archive', 'err')
                        continue
                    for _ in range(3):                      # nested archives
                        nested = [os.path.join(r, f) for r, _, fs in os.walk(xdir) for f in fs
                                  if f.lower().endswith(ARCHIVE_EXTS)]
                        if not nested:
                            break
                        for arc in nested:
                            extract_archive(arc, os.path.splitext(arc)[0], self.cfg, log)
                            os.remove(arc)
                    added += self._flatten(xdir, target, log)
            finally:
                shutil.rmtree(tmp, ignore_errors=True)
            if self.learned:
                log('Learned provider names: ' + ', '.join(f'{k.upper()} = {v}' for k, v in self.learned.items()))
                self.post(self._save_learned, dict(self.learned))
            log(f'Done: {added} subtitle file(s) in {target}', 'ok')
            self.post(self.dl_info.set, f'Downloaded {added} file(s)')
            self.post(self.dl_prog.configure, {'value': 100})
            if added and open_after:
                self.post(self._dl_open, target)
        except Exception as ex:
            log(f'Failed: {ex}', 'err')
            self.post(self.dl_info.set, 'Download failed')
        finally:
            self.post(lambda: self.dl_btn.state(['!disabled']))

    def _flatten(self, xdir, target, log):
        """Move subtitle files into target; provider sub-folder names go into the file name if needed."""
        n = 0
        for r, _, files in os.walk(xdir):
            for f in files:
                if not f.lower().endswith(SUB_EXTS):
                    continue
                src = os.path.join(r, f)
                stem, ext = os.path.splitext(f)
                rel = os.path.relpath(r, xdir).replace('\\', '/').split('/')
                folder = rel[-1] if len(rel) > 1 else ''
                code = provider_code(stem)
                if folder and code and code.lower() not in PROVIDERS and not re.fullmatch(r'S\d+|E\d+', folder):
                    PROVIDERS[code.lower()] = folder                 # learn "BB" -> "Blockbuster"
                    self.learned[code.lower()] = folder
                if folder and detect_provider(stem) == 'Unknown':
                    toks = stem.split('.')
                    toks.insert(len(toks) - 1 if len(toks) > 1 else 1, f'{folder.replace(" ", "")}.WEB')
                    stem = '.'.join(toks)
                dst = unique_path(os.path.join(target, stem + ext))
                shutil.move(src, dst)
                n += 1
        log(f'    {n} subtitle file(s) added')
        return n

    def _save_learned(self, names):
        self.cfg.setdefault('provider_names', {}).update(names)
        save_settings(self.cfg)

    def _dl_open(self, target):
        self.load(target)
        self.page.set('pick')
        self.show_page()

    # ---------- data ----------
    def pick_folder(self):
        d = filedialog.askdirectory(initialdir=self.folder or os.getcwd(), title='Subtitle folder')
        if d:
            self.load(d)

    def default_out(self, fmt):
        if fmt == 'srt':
            return os.path.join(self.folder, 'Selected')
        return os.path.join(self.folder, f'{self.title}.{fmt}')

    def on_fmt(self):
        fmt = self.out_fmt.get()
        cur = self.out_path.get().strip()
        if not cur or cur in {self.default_out(f) for f in ('srt', 'mks', 'mkv')}:
            self.out_path.set(self.default_out(fmt) if self.folder else '')
        elif fmt != 'srt':
            if cur.lower().endswith(('.mks', '.mkv')):
                self.out_path.set(os.path.splitext(cur)[0] + '.' + fmt)
            else:
                self.out_path.set(os.path.join(cur, f'{self.title}.{fmt}'))
        elif cur.lower().endswith(('.mks', '.mkv')):
            self.out_path.set(os.path.dirname(cur))
        self.num_chk.state(['!disabled'] if fmt == 'srt' else ['disabled'])
        self.update_status()

    def pick_out(self):
        fmt = self.out_fmt.get()
        cur = self.out_path.get().strip()
        if fmt == 'srt':
            d = filedialog.askdirectory(initialdir=cur or self.folder, title='Export SRT files to')
            if d:
                self.out_path.set(os.path.normpath(d))
        else:
            f = filedialog.asksaveasfilename(initialdir=os.path.dirname(cur) or self.folder,
                                             initialfile=os.path.basename(cur), defaultextension='.' + fmt,
                                             filetypes=[(f'Matroska {fmt.upper()}', '*.' + fmt)])
            if f:
                self.out_path.set(os.path.normpath(f))

    def load(self, folder, keep=False, newly=()):
        folder = os.path.normpath(os.path.abspath(folder))
        self.merge_tags_dir()
        paths = list_subs(folder)
        if not paths:
            messagebox.showwarning('SubPicker', f'No subtitle files found in:\n{folder}')
            return
        old_sel = dict(self.sel) if keep else {}
        self.folder = folder
        self.folder_lbl.config(text=folder)
        self.subs = [Sub(p) for p in paths]
        text_cues = [s.cues for s in self.subs if s.cues]
        med = statistics.median(text_cues) if text_cues else 1
        for s in self.subs:
            if s.cues and not s.is_forced and not s.is_sdh and s.cues < FORCED_RATIO * med:
                s.auto.add('forced')
        self.title = common_title([os.path.basename(p) for p in paths], os.path.basename(folder))
        present = sorted({s.provider for s in self.subs})
        if keep:
            self.prov_order += [p for p in present if p not in self.prov_order]
            for p in present:
                self.prov_on.setdefault(p, tk.BooleanVar(value=True))
            self.sel = {s.path: old_sel.get(s.path, s.path in newly) for s in self.subs}
            self.build_providers()
            self.rebuild()
            return
        self.overrides = {}
        self.no_sync = set()
        self.manual_order = None
        self.prov_order = [p for p in DEFAULT_PRIORITY if p in present] + \
                          [p for p in present if p not in DEFAULT_PRIORITY]
        self.prov_on = {p: tk.BooleanVar(value=True) for p in self.prov_order}
        self.sel = {s.path: False for s in self.subs}
        self.out_path.set('')
        self.on_fmt()
        self.build_providers()
        self.auto_select()

    def enabled(self):
        return [p for p in self.prov_order if self.prov_on[p].get()]

    def primary(self):
        en = self.enabled()
        return en[0] if en else None

    def groups(self):
        rank = {p: i for i, p in enumerate(self.enabled())}
        g = {}
        for s in self.subs:
            if s.provider in rank:
                g.setdefault(s.base, []).append(s)
        for items in g.values():
            items.sort(key=lambda s: (rank[s.provider], region_info(s)[0], s.is_sdh, s.is_forced, s.name))
        return g

    def auto_select(self):
        for k in self.sel:
            self.sel[k] = False
        for items in self.groups().values():
            best = items[0].provider
            seen = set()
            for s in items:
                key = (s.tag, s.is_sdh, s.is_forced)
                if s.provider == best and key not in seen:
                    seen.add(key)
                    self.sel[s.path] = True
        self.rebuild()

    def select_none(self):
        for k in self.sel:
            self.sel[k] = False
        self.refresh_marks()

    def chosen(self):
        en = set(self.enabled())
        return [s for s in self.subs if self.sel.get(s.path) and s.provider in en]

    # ---------- add subtitles ----------
    def on_drop(self, paths):
        """Files / folders dropped from Explorer."""
        subs, mkvs, dirs = [], [], []
        for p in paths:
            if os.path.isdir(p):
                dirs.append(p)
            elif p.lower().endswith(SUB_EXTS):
                subs.append(p)
            elif p.lower().endswith(('.mkv', '.mks', '.mka')):
                mkvs.append(p)
        if not self.folder:                     # nothing open yet: a dropped folder becomes the folder
            if dirs:
                self.load(dirs[0])
                self.page.set('pick')
                self.show_page()
                return
            if subs:
                self.load(os.path.dirname(subs[0]), newly=set(subs))
                self.page.set('pick')
                self.show_page()
                return
        for d_ in dirs:
            subs += [os.path.join(r, f) for r, _, fs in os.walk(d_) for f in fs if f.lower().endswith(SUB_EXTS)]
        if subs:
            self.add_files(subs)
            if self.page.get() == 'download':
                self.page.set('pick')
                self.show_page()
        for m in mkvs:
            self.extract_from_mkv(m)
        if not subs and not mkvs:
            self.status.set('Nothing to add - drop .srt / .ass / .ssa / .vtt / .sup files, a folder or an MKV.')

    def add_files(self, files=None):
        if not self.folder:
            messagebox.showinfo('SubPicker', 'Open or download a subtitle folder first.')
            return
        if files is None:
            files = filedialog.askopenfilenames(title='Add subtitle files', filetypes=[
                ('Subtitles', '*.srt *.ass *.ssa *.vtt *.sup'), ('All', '*.*')])
        if not files:
            return
        new = []
        for f in files:
            dst = os.path.join(self.folder, os.path.basename(f))
            if os.path.normcase(os.path.abspath(f)) != os.path.normcase(dst):
                dst = unique_path(dst)
                shutil.copy2(f, dst)
            new.append(dst)
        self.load(self.folder, keep=True, newly=set(new))

    def mkv_tools(self):
        """(mkvmerge, mkvextract) from Settings / auto-detect; asks for mkvextract.exe once if missing."""
        extract = resolve_tool(self.cfg.get('mkvextract')) or find_mkv_tool('mkvextract')
        if not extract:
            messagebox.showinfo('SubPicker', 'MKVToolNix was not found.\n\nPlease locate mkvextract.exe '
                                '(usually in C:\\Program Files\\MKVToolNix). You can also set it in Settings.')
            f = filedialog.askopenfilename(title='Locate mkvextract.exe',
                                           filetypes=[('mkvextract', 'mkvextract*.exe'), ('All', '*.*')])
            extract = os.path.normpath(f) if f else ''
        merge = resolve_tool(self.cfg.get('mkvmerge'))
        merge = find_mkv_tool('mkvmerge', near=extract) if extract and not merge else merge
        if not extract or not merge:
            if extract:
                messagebox.showwarning('SubPicker', 'mkvmerge.exe was not found next to mkvextract.exe.')
            return None, None
        if (self.cfg.get('mkvextract'), self.cfg.get('mkvmerge')) != (extract, merge):
            self.cfg['mkvextract'], self.cfg['mkvmerge'] = extract, merge
            save_settings(self.cfg)
        return merge, extract

    def extract_from_mkv(self, mkv=None):
        """Extract subtitle tracks from any MKV. By default into a new folder named after the file,
        which is then opened in Pick subtitles; or into the folder that is open now."""
        mkv = mkv or filedialog.askopenfilename(title='Video with subtitle tracks',
                                                filetypes=[('Matroska', '*.mkv *.mks *.mka'), ('All', '*.*')])
        if not mkv:
            return
        merge, extract = self.mkv_tools()
        if not merge:
            return
        if not mkv:
            return
        p = subprocess.run([merge, '-J', mkv], capture_output=True, encoding='utf-8', errors='replace',
                           creationflags=NO_WINDOW)
        try:
            info = json.loads(p.stdout)
        except ValueError:
            messagebox.showerror('SubPicker', 'mkvmerge could not read the file.')
            return
        ext_by_codec = {'S_TEXT/UTF8': '.srt', 'S_TEXT/ASCII': '.srt', 'S_TEXT/ASS': '.ass',
                        'S_TEXT/SSA': '.ssa', 'S_TEXT/WEBVTT': '.vtt', 'S_HDMV/PGS': '.sup'}
        tracks = [t for t in info.get('tracks', []) if t.get('type') == 'subtitles']
        if not tracks:
            messagebox.showinfo('SubPicker', 'This file has no subtitle tracks.')
            return
        d = Dialog(self, 'Extract subtitle tracks', 860, 680)
        s = self.scale
        vars_ = []
        extract_btn = [None]

        # --- top bar: file name + quick selection
        top = tk.Frame(d.body, bg=C['panel'])
        top.pack(fill='x', pady=(0, 10))
        tk.Label(top, text=f'{len(tracks)} subtitle track' + ('s' if len(tracks) != 1 else ''), bg=C['panel'],
                 fg=C['fg'], font=self.f_bold).pack(side='left')
        tk.Label(top, text='  ·  ' + fit(tkfont.Font(font=self.f_base), os.path.basename(mkv), int(420 * s)),
                 bg=C['panel'], fg=C['muted'], font=self.f_base).pack(side='left')

        def pick(rule):
            for t, v, ext, lang, pr in vars_:
                if ext:
                    v.set(rule(ext, lang, pr))

        for txt, rule in (('None', lambda e, l, p: False), ('All', lambda e, l, p: True)):
            ttk.Button(top, text=txt, command=lambda r=rule: pick(r)).pack(side='right', padx=(6, 0))

        # --- one card per track; click anywhere on it to tick / untick
        lst = tk.Frame(d.body, bg=C['panel'])
        lst.pack(fill='both', expand=True)
        chip_col = {'SDH': C['accent'], 'FORCED': C['edit'], 'DEFAULT': C['ok']}

        def chip(parent, text, fg, bg):
            return tk.Label(parent, text=f' {text} ', bg=C['pill_bg'], fg=fg, font=self.f_small_bold,
                            padx=int(4 * s), pady=0)

        for t in tracks:
            pr = t.get('properties', {})
            ext = ext_by_codec.get(pr.get('codec_id', ''))
            lang = pr.get('language_ietf') or pr.get('language') or 'und'
            v = tk.BooleanVar(value=bool(ext))
            row = tk.Frame(lst, bg=C['field'], highlightthickness=1, highlightbackground=C['border'],
                           cursor='hand2' if ext else '')
            row.pack(fill='x', pady=(0, int(3 * s)))
            inner = tk.Frame(row, bg=C['field'])
            inner.pack(fill='x', padx=int(10 * s), pady=int(3 * s))
            box = tk.Label(inner, bg=C['field'])
            box.pack(side='left', padx=(0, int(10 * s)))
            tk.Label(inner, text=f"#{t['id']}", bg=C['field'], fg=C['faint'], font=self.f_mono,
                     width=4, anchor='w').pack(side='left')
            tk.Label(inner, text=f' {lang} ', bg=C['pill_bg'], fg=C['pill_fg'], font=self.f_small_bold,
                     padx=int(2 * s)).pack(side='left', padx=(0, int(10 * s)))
            text = tk.Frame(inner, bg=C['field'])        # one line: language — name   ·   format · lines · size
            text.pack(side='left', fill='x', expand=True)
            title = lang_display(lang) + (f'  —  {pr["track_name"]}' if pr.get('track_name') else '')
            tk.Label(text, text=title, bg=C['field'], fg=C['fg'] if ext else C['faint'], font=self.f_bold,
                     anchor='w').pack(side='left')
            info = [t.get('codec', '') or pr.get('codec_id', '')]
            lines = pr.get('tag_number_of_frames') or pr.get('num_index_entries')
            if lines:
                info.append(f'{int(lines):,} lines' if ext != '.sup' else f'{int(lines):,} images')
            if pr.get('tag_number_of_bytes'):
                info.append(f"{int(pr['tag_number_of_bytes']) / 1024:,.0f} KB")
            if not ext:
                info.append('not supported - cannot be extracted')
            tk.Label(text, text='  ·  '.join(info), bg=C['field'], fg=C['err'] if not ext else C['muted'],
                     font=self.f_small, anchor='w').pack(side='left', padx=(int(14 * s), 0))
            chips = tk.Frame(inner, bg=C['field'])
            chips.pack(side='right')
            for flag, on in (('DEFAULT', pr.get('default_track')), ('FORCED', pr.get('forced_track')),
                             ('SDH', pr.get('flag_hearing_impaired'))):
                if on:
                    chip(chips, flag, chip_col[flag], C['field']).pack(side='left', padx=(int(4 * s), 0))

            def paint(*_, v=v, row=row, box=box, ext=ext):
                on = v.get() and bool(ext)
                box.configure(image=self.box('on' if on else 'off', C['field']))
                row.configure(highlightbackground=C['accent'] if on else C['border'])
                n = sum(1 for _t, vv, e, *_r in vars_ if e and vv.get())
                if extract_btn[0]:
                    extract_btn[0].configure(text=f'Extract {n} track' + ('s' if n != 1 else '') if n
                                             else 'Extract')
                    extract_btn[0].state(['!disabled'] if n else ['disabled'])

            v.trace_add('write', paint)
            if ext:
                def toggle(_e=None, v=v):
                    v.set(not v.get())
                for w in [row, inner, box, text, chips] + list(inner.winfo_children()) + \
                        list(text.winfo_children()) + list(chips.winfo_children()):
                    w.bind('<Button-1>', toggle)
            vars_.append((t, v, ext, lang, pr))
            paint()

        # --- save to: in the fixed footer, always visible
        stem = os.path.splitext(os.path.basename(mkv))[0]
        new_dir = os.path.join(os.path.dirname(os.path.abspath(mkv)), stem)
        if os.path.isfile(new_dir):
            new_dir += ' Subs'
        # editable: type any folder (created if missing), Browse for one, or use the folder open now
        dest = tk.StringVar(value=new_dir)
        foot = ttk.Frame(d.buttons)
        foot.pack(side='top', fill='x', pady=(0, 10))
        foot.columnconfigure(1, weight=1)
        ttk.Label(foot, text='SAVE TO', style='Muted.TLabel').grid(row=0, column=0, padx=(2, 10))
        ttk.Entry(foot, textvariable=dest).grid(row=0, column=1, sticky='ew')

        def browse_dest():
            cur = dest.get().strip()
            start = cur if os.path.isdir(cur) else os.path.dirname(cur) or os.path.dirname(mkv)
            f = filedialog.askdirectory(parent=d, title='Save extracted subtitles to', initialdir=start)
            if f:
                dest.set(os.path.normpath(f))

        ttk.Button(foot, text='Browse…', command=browse_dest).grid(row=0, column=2, padx=(6, 0))
        ttk.Button(foot, text='Next to MKV', command=lambda: dest.set(new_dir)).grid(row=0, column=3, padx=(6, 0))
        if self.folder:
            ttk.Button(foot, text='Open folder', command=lambda: dest.set(self.folder)).grid(
                row=0, column=4, padx=(6, 0))

        def go():
            todo = [(t, ext, lang, pr) for t, v, ext, lang, pr in vars_ if v.get() and ext]
            out_dir = os.path.normpath(os.path.expandvars(dest.get().strip().strip('"')))
            if not dest.get().strip():
                messagebox.showinfo('SubPicker', 'Choose a folder to save to.', parent=d)
                return
            if not os.path.isabs(out_dir):              # a bare name = a folder next to the MKV
                out_dir = os.path.join(os.path.dirname(os.path.abspath(mkv)), out_dir)
            into_open = bool(self.folder) and os.path.normcase(out_dir) == os.path.normcase(
                os.path.normpath(self.folder))
            d.destroy()
            if not todo:
                return
            try:
                os.makedirs(out_dir, exist_ok=True)
            except OSError as ex:
                messagebox.showerror('SubPicker', f'Could not create the folder:\n{out_dir}\n\n{ex}')
                return
            specs, outs = [], []
            for t, ext, lang, pr in todo:
                fl = ('[sdh]' if pr.get('flag_hearing_impaired') else '') + ('[forced]' if pr.get('forced_track') else '')
                out = unique_path(os.path.join(out_dir, f"{stem}_track{t['id']}_[{lang}]{fl}{ext}"))
                specs.append(f"{t['id']}:{out}")
                outs.append(out)

            def done(ok):
                got = {o for o in outs if os.path.isfile(o)}
                if not got:
                    return
                if into_open:
                    self.load(self.folder, keep=True, newly=got)
                else:
                    self.load(out_dir, newly=got)
                self.page.set('pick')
                self.show_page()

            self.run_task('Extracting subtitle tracks', [extract, '--gui-mode', mkv, 'tracks'] + specs,
                          on_done=done)

        extract_btn[0] = ttk.Button(d.buttons, text='Extract', style='Accent.TButton', command=go)
        extract_btn[0].pack(side='right')
        vars_[0][1].set(vars_[0][1].get())          # refresh the "Extract N tracks" label
        ttk.Button(d.buttons, text='Cancel', command=d.destroy).pack(side='right', padx=(0, 6))

    def run_task(self, title, cmd, on_done=None):
        """Run a command in the background with a progress window."""
        d = Dialog(self, title, 720, 380)
        pb = ttk.Progressbar(d.body, maximum=100)
        pb.pack(fill='x')
        log = log_text(d.body, height=10, font=self.f_mono)
        log.pack(fill='both', expand=True, pady=(10, 0))
        close = ttk.Button(d.buttons, text='Close', command=d.destroy)
        close.pack(side='right')
        close.state(['disabled'])

        def run():
            rc = -1
            try:
                p = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8',
                                     errors='replace', creationflags=NO_WINDOW)
                for line in p.stdout:
                    line = line.rstrip()
                    m = re.match(r'#GUI#progress (\d+)%', line)
                    if m:
                        self.post(pb.configure, {'value': int(m.group(1))})
                    elif line and not line.startswith('#GUI#'):
                        self.post(text_write, log, line)
                rc = p.wait()
            except OSError as ex:
                self.post(text_write, log, str(ex), 'err')
            ok = rc in (0, 1)
            self.post(text_write, log, 'Done.' if ok else f'Failed (exit code {rc}).', 'ok' if ok else 'err')
            self.post(pb.configure, {'value': 100 if ok else 0})
            self.post(lambda: close.state(['!disabled']))
            if on_done:
                self.post(on_done, ok)

        threading.Thread(target=run, daemon=True).start()

    # ---------- page 1 ----------
    def rebuild(self):
        opened = {i: self.tree.item(i, 'open') for i in self.tree.get_children()}
        y = self.tree.yview()[0]
        self.tree.delete(*self.tree.get_children())
        prim = self.primary()
        groups = self.groups()
        view = self.view.get()

        def has_prim(b):
            return any(s.provider == prim for s in groups[b])

        def mkv_key(b):
            return (b != 'en', family(b).lower(), b == 'yue', b)

        if self.sort_mode.get() == 'missing':
            order = sorted(groups, key=lambda b: (has_prim(b), mkv_key(b)))
        else:
            order = sorted(groups, key=mkv_key)
        only = getattr(self, 'prov_filter', None)          # double-clicked provider, None = all
        for base in order:
            items = groups[base]
            hp = has_prim(base)
            if (view == 'missing' and hp) or (view == 'primary' and not hp):
                continue
            provs = list(dict.fromkeys(s.provider for s in items))
            shown = [s for s in items if s.provider == only] if only else items
            if not shown:
                continue
            gid = 'L:' + base
            self.tree.insert('', 'end', iid=gid, text=f' {lang_name(base)}  ·  {base}',
                             values=(f'●  In {prim}' if hp else f'●  Not in {prim}', '', '', '',
                                     'Available from  ' + ', '.join(provs)),
                             open=True if only else opened.get(gid, not hp),
                             tags=('have' if hp else 'missing',))
            for s in shown:
                self.tree.insert(gid, 'end', iid='F:' + s.path, text='',
                                 values=(s.provider, s.variant, s.cues or '—', f'{s.size / 1024:.0f} KB',
                                         os.path.relpath(s.path, self.folder)))
        self.tree.yview_moveto(y)
        self.refresh_marks()

    def refresh_marks(self):
        for gid in self.tree.get_children():
            kids = self.tree.get_children(gid)
            n = 0
            for k in kids:
                on = self.sel[k[2:]]
                n += on
                self.tree.item(k, image=self.box('on' if on else 'off', C['panel']),
                               tags=('on' if on else 'off',))
            bg = C['have_bg'] if 'have' in self.tree.item(gid, 'tags') else C['miss_bg']
            state = 'on' if kids and n == len(kids) else ('part' if n else 'off')
            v = list(self.tree.item(gid, 'values'))
            v[1] = f'{n} selected' if n else ''
            self.tree.item(gid, image=self.box(state, bg), values=v)
        self.update_status()
        self.refresh_tracks()

    def update_status(self):
        if not self.subs:
            return
        prim = self.primary()
        g = self.groups()
        missing = sorted(b for b, items in g.items() if not any(s.provider == prim for s in items))
        chosen = self.chosen()
        auto = sum(1 for s in self.subs if s.auto)
        self.status.set(
            f'{len(self.subs)} files  ·  {len(g)} languages  ·  primary {prim}  ·  '
            f'{len(missing)} not in {prim}: {", ".join(missing) or "–"}  ·  '
            f'{auto} flagged from content  ·  selected {len(chosen)} files / '
            f'{len({s.base for s in chosen})} languages')
        fmt = self.out_fmt.get()
        self.export_btn.config(text=f'Copy {len(chosen)} files' if fmt == 'srt'
                               else f'Create {fmt.upper()}  ·  {len(chosen)} tracks')

    def toggle(self, iid):
        if iid.startswith('F:'):
            self.sel[iid[2:]] = not self.sel[iid[2:]]
        elif iid.startswith('L:'):
            kids = [k[2:] for k in self.tree.get_children(iid)]
            target = not all(self.sel[k] for k in kids)
            for k in kids:
                self.sel[k] = target

    def on_click(self, e):
        iid = self.tree.identify_row(e.y)
        if not iid or self.tree.identify_region(e.x, e.y) not in ('cell', 'tree'):
            return
        if iid.startswith('F:'):
            self.toggle(iid)
            self.refresh_marks()
        else:
            try:
                elem = self.tree.identify_element(e.x, e.y)
            except tk.TclError:
                elem = ''
            if 'image' in elem:
                self.toggle(iid)
                self.refresh_marks()
                return 'break'

    def on_space(self, _):
        for iid in self.tree.selection():
            self.toggle(iid)
        self.refresh_marks()
        return 'break'

    def on_double(self, e):
        iid = self.tree.identify_row(e.y)
        if iid.startswith('F:'):
            self.toggle(iid)          # undo the toggle from the second click
            self.refresh_marks()
            self.open_file(iid[2:])

    def open_file(self, path):
        try:
            os.startfile(path)
        except (AttributeError, OSError) as ex:
            messagebox.showerror('SubPicker', str(ex))

    def expand(self, state):
        for i in self.tree.get_children():
            self.tree.item(i, open=state)

    # ---------- page 2: track order ----------
    def tracks(self):
        prank = {p: i for i, p in enumerate(self.prov_order)}

        def key(s):
            vr, _, rk = region_info(s)
            return (s.base != 'en', family(s.base).lower(), (100 if s.base == 'yue' else 0) + vr, rk,
                    (1 if s.is_sdh else 0) + (2 if s.is_forced else 0), prank.get(s.provider, 99),
                    s.name.lower())

        seen, out = {}, []
        for i, s in enumerate(sorted(self.chosen(), key=key)):
            vr, rtitle, rk = region_info(s)
            gk = (s.base, vr, rk, s.is_sdh, s.is_forced)
            n = seen.get(gk, 0)
            seen[gk] = n + 1
            parts = (['Forced'] if s.is_forced else []) + (['SDH'] if s.is_sdh else []) + \
                    ([rtitle] if rtitle else [])
            if n:
                parts.append('Alternate' if n == 1 else f'Alternate {n}')
            t = {'path': s.path, 'sub': s, 'lang': s.tag, 'name': ' / '.join(parts),
                 'sdh': s.is_sdh, 'forced': s.is_forced, 'default': False, 'delay': 0,
                 'tags': self.auto_tag(s), 'synced': '', 'i': i}
            t.update(self.overrides.get(s.path, {}))
            if t['synced'] and not os.path.isfile(t['synced']):
                t['synced'] = ''
            t['src'] = t['synced'] or s.path
            out.append(t)
        if self.manual_order:
            idx = {p: n for n, p in enumerate(self.manual_order)}
            out.sort(key=lambda t: (idx.get(t['path'], len(idx) + t['i'])))
        return out

    def track(self, path):
        return next(t for t in self.tracks() if t['path'] == path)

    def auto_tag(self, sub):
        """Source tags XML saved in Settings for this subtitle's provider ('' if none)."""
        table = {k.lower(): v for k, v in (self.cfg.get('source_tags') or {}).items()}
        for k in provider_keys(sub):
            p = table.get(k)
            if p and os.path.isfile(p):
                return p
        return ''

    def merge_tags_dir(self):
        """Pick up new '<code> Source Tag.xml' files from the tags folder (never overwrites)."""
        folder = self.cfg.get('tags_dir', '')
        if not folder or not os.path.isdir(folder):
            return
        table = self.cfg.setdefault('source_tags', {})
        found, _ = scan_tags_dir(folder)
        have = {k.lower() for k in table}
        new = {k: v for k, v in found.items() if k.lower() not in have}
        if new:
            table.update(new)
            save_settings(self.cfg)

    def refresh_tracks(self):
        if not hasattr(self, 'table'):
            return
        ts = self.tracks()
        rows = []
        for n, t in enumerate(ts, 1):
            tags = t['tags']
            rows.append({
                'id': 'T:' + t['path'], 'n': str(n), 'sync': t['path'] not in self.no_sync and not t['sub'].pgs,
                'lang': t['lang'], 'lname': lang_display(t['lang']),
                'name': t['name'], 'sdh': t['sdh'], 'forced': t['forced'], 'default': t['default'],
                'delay': f"{t['delay']:+d} ms" if t['delay'] else '',
                'tags': ('XML · ' + os.path.basename(tags)) if is_xml_path(tags) else tags,
                'provider': t['sub'].provider,
                'file': ('⟳ synced · ' if t['synced'] else '') + t['sub'].name,
                'accent_key': 'file' if t['synced'] else None, 'accent': C['sync'],
                'changed': set(self.overrides.get(t['path'], {})),
            })
        self.table.set_rows(rows)
        edited = sum(1 for p in self.overrides if self.overrides[p])
        synced = sum(1 for t in ts if t['synced'])
        self.order_info.set(f'{len(ts)} tracks  ·  ' + ('manual order' if self.manual_order else 'auto sorted') +
                            (f'  ·  {edited} edited' if edited else '') + (f'  ·  {synced} synced' if synced else ''))
        self.tabs.set_label('order', f'Track order  ·  {len(ts)}')

    def set_override(self, paths, key, value):
        for p in paths:
            self.overrides.setdefault(p, {})[key] = value
        self.refresh_tracks()

    def toggle_flag(self, rid, key):
        path = rid[2:]
        targets = [r[2:] for r in self.table.selection()] if rid in self.table.sel else [path]
        if key == 'sync':
            if self.track(path)['sub'].pgs:
                self.status.set('PGS (.sup) tracks are never synced - use the DELAY column to shift them.')
                return
            if path in self.no_sync:
                self.no_sync.difference_update(targets)
            else:
                self.no_sync.update(targets)
            self.refresh_tracks()
            return
        new = not self.track(path)[key]
        self.set_override(targets, key, new)

    def set_sync(self, paths=None):
        """Tick SYNC for `paths` only (None = tick all)."""
        all_p = [t['path'] for t in self.tracks()]
        self.no_sync = set() if paths is None else set(all_p) - set(paths)
        self.refresh_tracks()

    def on_table_head(self, key):
        if key == 'sync':                       # header click: tick all / untick all
            ts = [t for t in self.tracks() if not t['sub'].pgs]
            all_on = all(t['path'] not in self.no_sync for t in ts)
            self.set_sync([] if all_on else None)

    def auto_sort(self):
        self.manual_order = None
        self.refresh_tracks()

    def reset_overrides(self):
        if any('synced' in o for o in self.overrides.values()) and not messagebox.askyesno(
                'SubPicker', 'This also forgets the synced copies (the files stay on disk). Continue?'):
            return
        self.overrides = {}
        self.refresh_tracks()

    def reset_rows(self, rids):
        for rid in rids:
            self.overrides.pop(rid[2:], None)
        self.refresh_tracks()

    def move_track(self, d):
        sel = [i[2:] for i in self.table.selection()]
        if not sel:
            return
        order = [t['path'] for t in self.tracks()]
        idxs = sorted(order.index(p) for p in sel)
        if d < 0:
            for i in idxs:
                if i > 0 and order[i - 1] not in sel:
                    order[i - 1], order[i] = order[i], order[i - 1]
        else:
            for i in reversed(idxs):
                if i < len(order) - 1 and order[i + 1] not in sel:
                    order[i + 1], order[i] = order[i], order[i + 1]
        self.manual_order = order
        self.refresh_tracks()
        self.table.see('T:' + sel[0])

    def edit_cell(self, rid, key):
        path = rid[2:]
        cur = self.track(path)[key]
        value = str(cur) if key != 'delay' else (str(cur) if cur else '')
        self.table.edit_entry(rid, key, value, lambda v: self.commit_edit(path, key, v))

    def commit_edit(self, path, key, v):
        v = v.strip()
        if key == 'delay':
            try:
                v = int(v.replace('ms', '').replace('+', '').strip() or 0)
            except ValueError:
                messagebox.showerror('SubPicker', 'Delay must be a whole number of milliseconds.')
                return
        elif key == 'lang':
            if not LANG_TAG_RE.fullmatch(v):
                messagebox.showerror('SubPicker', f'"{v}" is not a valid language tag (e.g. en, en-GB, zh-Hans).')
                return
        elif key == 'tags':
            v = v.strip('"')
            if v.lower().endswith('.xml') and not os.path.isfile(v):
                messagebox.showerror('SubPicker', f'Tags file not found:\n{v}')
                return
        self.set_override([path], key, v)          # '' = no tag (also hides the provider XML)

    def choose_tags(self, rids):
        if not rids:
            return
        f = filedialog.askopenfilename(title='Tags XML for the selected track(s)', initialdir=self.folder,
                                       filetypes=[('Matroska tags XML', '*.xml'), ('All', '*.*')])
        if f:
            self.set_override([r[2:] for r in rids], 'tags', os.path.normpath(f))

    def clear_tags(self, rids):
        for rid in rids:
            self.overrides.setdefault(rid[2:], {})['tags'] = ''
        self.refresh_tracks()

    def ask_source_text(self, rids):
        if not rids:
            return
        from tkinter import simpledialog
        cur = self.track(rids[0][2:])['tags']
        v = simpledialog.askstring('Source tag', f'Source text for {len(rids)} track(s):',
                                   initialvalue='' if is_xml_path(cur) else cur, parent=self.root)
        if v is None:
            return
        if v.strip():
            self.set_override([r[2:] for r in rids], 'tags', v.strip())
        else:
            self.clear_tags(rids)

    def ask_delay(self, rids):
        if not rids:
            return
        from tkinter import simpledialog
        v = simpledialog.askinteger('Delay', f'Delay in ms for {len(rids)} track(s) (negative = earlier):',
                                    initialvalue=self.track(rids[0][2:])['delay'], parent=self.root)
        if v is not None:
            self.set_override([r[2:] for r in rids], 'delay', v)

    def source_from_provider(self, rids):
        for rid in rids:
            prov = next((s.provider for s in self.subs if s.path == rid[2:]), '')
            if prov:
                self.overrides.setdefault(rid[2:], {})['tags'] = prov if prov == 'Blu-ray' else f'{prov} WEB-DL'
        self.refresh_tracks()

    def undo_sync(self, rids):
        for rid in rids:
            self.overrides.get(rid[2:], {}).pop('synced', None)
        self.refresh_tracks()

    # ---------- sync ----------
    def sync_dialog(self):
        all_ts = self.tracks()
        if not all_ts:
            messagebox.showinfo('SubPicker', 'No tracks selected on the Pick subtitles page.')
            return
        # what gets synced = the SYNC tick boxes (clicking / selecting rows never changes it)
        ts = [t for t in all_ts if not t['sub'].pgs]          # PGS (image) subs are never synced
        targets = [t for t in ts if t['path'] not in self.no_sync]
        n_pgs = len(all_ts) - len(ts)
        if not ts:
            messagebox.showinfo('SubPicker', 'All tracks are PGS (.sup) - those are not synced.\n'
                                'Use the DELAY column to shift them instead.')
            return
        if not targets:
            messagebox.showinfo('SubPicker', 'No track is ticked in the SYNC column.\n'
                                'Tick the tracks to sync (click the SYNC header to tick all).')
            return
        d = Dialog(self, 'Sync subtitles', 820, 600)
        b = d.body
        b.columnconfigure(1, weight=1)
        ttk.Label(b, text='TRACKS', style='CardHead.TLabel').grid(row=0, column=0, sticky='w')
        n_t = len(targets)
        ttk.Label(b, style='Card.TLabel', text=(f'All {n_t} tracks' if n_t == len(ts) else
                                                f'{n_t} of {len(ts)} tracks (SYNC ticked)') +
                  (f'   ·   {n_pgs} PGS excluded' if n_pgs else '')
                  ).grid(row=0, column=1, columnspan=2, sticky='w')
        # reference: an outside file (usual case) or one of the tracks
        ttk.Label(b, text='REFERENCE', style='CardHead.TLabel').grid(row=1, column=0, sticky='w', pady=(12, 0))
        ref_mode = tk.StringVar(value='file')
        ref_box = tk.Frame(b, bg=C['panel'])
        ref_box.grid(row=2, column=0, columnspan=3, sticky='ew', pady=(6, 0))
        ref_box.columnconfigure(0, weight=1)
        ref_path = tk.StringVar()
        file_row = tk.Frame(ref_box, bg=C['panel'])
        file_row.columnconfigure(0, weight=1)
        ttk.Entry(file_row, textvariable=ref_path).grid(row=0, column=0, sticky='ew')

        def browse_ref():
            start_dir = self.cfg.get('ref_dir', '')
            if ref_path.get().strip():
                start_dir = os.path.dirname(ref_path.get().strip())
            f = filedialog.askopenfilename(
                parent=d, title='Reference (video, audio or a synced subtitle)',
                initialdir=start_dir if os.path.isdir(start_dir) else None,
                filetypes=[('Video / audio / subtitle',
                            '*.mkv *.mp4 *.m2ts *.ts *.avi *.mov *.webm *.mka *.ac3 *.eac3 *.dts *.flac '
                            '*.aac *.m4a *.wav *.srt *.ass *.ssa *.vtt'), ('All files', '*.*')])
            if f:
                ref_path.set(os.path.normpath(f))
        ttk.Button(file_row, text='Browse…', command=browse_ref).grid(row=0, column=1, padx=(6, 0))

        choices = [f"#{n}  {t['lang']}  ·  {t['sub'].provider}  ·  {t['name'] or 'no name'}  ·  {t['sub'].name}"
                   for n, t in enumerate(ts, 1)]
        en = next((n for n, t in enumerate(ts) if t['sub'].base == 'en'), 0)
        ref_var = tk.StringVar(value=choices[en])
        cb = ttk.Combobox(ref_box, textvariable=ref_var, values=choices, state='readonly')

        def on_mode(*_):
            if ref_mode.get() == 'file':
                cb.grid_remove()
                file_row.grid(row=0, column=0, sticky='ew')
            else:
                file_row.grid_remove()
                cb.grid(row=0, column=0, sticky='ew')
        Segmented(b, self, [('file', 'Outside file'), ('track', 'A track in this list')], ref_mode,
                  command=on_mode, bg=C['panel']).grid(row=1, column=1, sticky='w', pady=(12, 0))
        on_mode()

        tool = tk.StringVar(value=self.cfg.get('sync_tool', 'alass'))
        method = tk.StringVar(value=self.cfg.get('sync_method', 'batch'))
        ttk.Label(b, text='RUN', style='CardHead.TLabel').grid(row=3, column=0, sticky='w', pady=(14, 0))
        Segmented(b, self, [('batch', 'Batch sync'), ('direct', 'Single file')],
                  method, bg=C['panel']).grid(row=3, column=1, sticky='w', pady=(14, 0))
        ttk.Label(b, text='TOOL', style='CardHead.TLabel').grid(row=4, column=0, sticky='w', pady=(12, 0))
        Segmented(b, self, [('alass', 'alass'), ('ffsubsync', 'ffsubsync')], tool,
                  bg=C['panel']).grid(row=4, column=1, sticky='w', pady=(12, 0))
        out_dir = os.path.join(self.folder, 'Synced')
        ttk.Label(b, style='CardMuted.TLabel', wraplength=int(760 * self.scale), justify='left',
                  text=f'Synced copies are written to {out_dir} and used for export. '
                  'Originals are not changed. batchsync.py only handles .srt.'
                  ).grid(row=5, column=0, columnspan=3, sticky='w', pady=(10, 0))
        pb = ttk.Progressbar(b, maximum=100)
        pb.grid(row=6, column=0, columnspan=3, sticky='ew', pady=(12, 0))
        log = log_text(b, height=11, font=self.f_mono)
        log.grid(row=7, column=0, columnspan=3, sticky='nsew', pady=(10, 0))
        b.rowconfigure(7, weight=1)

        def start():
            if ref_mode.get() == 'file':
                ref = ref_path.get().strip().strip('"')
                if not ref:
                    browse_ref()
                    ref = ref_path.get().strip()
                    if not ref:
                        return
            else:
                ref = ts[choices.index(ref_var.get())]['src']
            if not os.path.isfile(ref):
                messagebox.showerror('SubPicker', 'Reference file not found.', parent=d)
                return
            if ref_mode.get() == 'file':
                self.cfg['ref_dir'] = os.path.dirname(os.path.abspath(ref))
            self.cfg['sync_tool'], self.cfg['sync_method'] = tool.get(), method.get()
            save_settings(self.cfg)
            jobs = [t for t in targets if os.path.normcase(t['src']) != os.path.normcase(ref)]
            start_btn.state(['disabled'])
            threading.Thread(target=self._sync_run, daemon=True,
                             args=(jobs, ref, tool.get(), method.get(), out_dir, log, pb,
                                   lambda: start_btn.state(['!disabled']))).start()

        start_btn = ttk.Button(d.buttons, text='Start sync', style='Accent.TButton', command=start)
        start_btn.pack(side='right')
        ttk.Button(d.buttons, text='Close', command=d.destroy).pack(side='right', padx=(0, 6))

    def _sync_run(self, jobs, ref, tool, method, out_dir, log, pb, finished):
        say = lambda text, tag=None: self.post(text_write, log, text, tag)  # noqa: E731
        results = {}
        try:
            os.makedirs(out_dir, exist_ok=True)
            exe = resolve_tool(self.cfg.get(tool))
            say(f'Reference: {ref}', 'head')
            if method == 'batch':
                bs = self.cfg.get('batchsync', '')
                py = python_cmd()
                if not os.path.isfile(bs):
                    raise RuntimeError('Set the batchsync.py path in Settings.')
                srt = [t for t in jobs if t['src'].lower().endswith('.srt')]
                for t in jobs:
                    if t not in srt:
                        say(f'skip (not .srt): {t["sub"].name}', 'err')
                tin, tout = tempfile.mkdtemp(prefix='sp_in_'), tempfile.mkdtemp(prefix='sp_out_')
                try:
                    names = {}
                    for t in srt:
                        nm = os.path.basename(unique_path(os.path.join(tin, t['sub'].name)))
                        shutil.copy2(t['src'], os.path.join(tin, nm))
                        names[nm] = t
                    say(f'Running batchsync.py ({tool}) on {len(names)} file(s)…')
                    self.post(pb.configure, {'mode': 'indeterminate'})
                    self.post(pb.start, 15)
                    p = subprocess.run([py, bs, '--use', tool, ref, tin, tout], capture_output=True,
                                       encoding='utf-8', errors='replace', creationflags=NO_WINDOW)
                    for line in (p.stdout + p.stderr).splitlines():
                        if line.strip() and not re.search(r'\d+ / \d+ \[|^\s*$', line):
                            say('  ' + line.strip(), 'err' if 'rror' in line else None)
                    for nm, t in names.items():
                        out = os.path.join(tout, nm)
                        if os.path.isfile(out) and os.path.getsize(out):
                            dst = os.path.join(out_dir, t['sub'].name)
                            shutil.move(out, dst)
                            results[t['path']] = dst
                finally:
                    shutil.rmtree(tin, ignore_errors=True)
                    shutil.rmtree(tout, ignore_errors=True)
                    self.post(pb.stop)
                    self.post(pb.configure, {'mode': 'determinate', 'value': 100})
            else:
                if not exe:
                    raise RuntimeError(f'{tool} not found - set its path in Settings.')
                for n, t in enumerate(jobs, 1):
                    dst = os.path.join(out_dir, t['sub'].name)
                    cmd = [exe, ref, t['src'], dst] if tool == 'alass' else [exe, ref, '-i', t['src'], '-o', dst]
                    say(f'[{n}/{len(jobs)}] {t["sub"].name}')
                    p = subprocess.run(cmd, capture_output=True, encoding='utf-8', errors='replace',
                                       creationflags=NO_WINDOW)
                    if p.returncode == 0 and os.path.isfile(dst) and os.path.getsize(dst):
                        results[t['path']] = dst
                        say('    ok', 'ok')
                    else:
                        msg = (p.stderr or p.stdout).strip().splitlines()
                        say('    failed: ' + (msg[-1] if msg else f'exit code {p.returncode}'), 'err')
                    self.post(pb.configure, {'value': n / len(jobs) * 100})
            say(f'Synced {len(results)} of {len(jobs)} track(s).', 'ok' if results else 'err')
        except Exception as ex:
            say(f'Failed: {ex}', 'err')
        finally:
            def apply():
                for path, dst in results.items():
                    self.overrides.setdefault(path, {})['synced'] = dst
                self.refresh_tracks()
                finished()
            self.post(apply)

    # ---------- export ----------
    def export(self):
        tracks = self.tracks()
        if not tracks:
            messagebox.showinfo('SubPicker', 'Nothing selected.')
            return
        out = self.out_path.get().strip()
        if not out:
            messagebox.showwarning('SubPicker', 'Choose where to export first.')
            return
        if self.out_fmt.get() == 'srt':
            self.export_srt(tracks, out)
        else:
            self.export_mkv(tracks, out)

    def export_srt(self, tracks, out):
        if os.path.normcase(os.path.abspath(out)) == os.path.normcase(self.folder):
            messagebox.showwarning('SubPicker', 'The export folder must differ from the source folder.')
            return
        plan = []
        for i, t in enumerate(tracks, 1):
            name = t['sub'].name
            plan.append((t, f'{i:02d}.{name}' if self.numbered.get() else name))
        os.makedirs(out, exist_ok=True)
        existing = [n for _, n in plan if os.path.exists(os.path.join(out, n))]
        if existing and not messagebox.askyesno(
                'SubPicker', f'{len(existing)} file(s) already exist in the export folder.\nOverwrite them?'):
            return
        shifted = 0
        for t, name in plan:
            dst = os.path.join(out, name)
            if t['delay'] and t['src'].lower().endswith('.sup'):
                shift_pgs(t['src'], dst, t['delay'])
                shifted += 1
            elif t['delay']:
                shift_file(t['src'], dst, t['delay'])
                shifted += 1
            else:
                shutil.copy2(t['src'], dst)
        msg = f'Copied {len(plan)} file(s) to:\n{out}'
        if shifted:
            msg += f'\n\nDelay applied to {shifted} file(s).'
        if messagebox.askyesno('SubPicker', msg + '\n\nOpen the folder?'):
            self.open_file(out)

    def get_mkvmerge(self):
        exe = resolve_tool(self.cfg.get('mkvmerge'))
        if exe:
            return exe
        messagebox.showinfo('SubPicker', 'mkvmerge.exe (MKVToolNix) was not found. Please locate it.')
        f = filedialog.askopenfilename(title='Locate mkvmerge.exe',
                                       filetypes=[('mkvmerge', 'mkvmerge*'), ('All', '*.*')])
        if f:
            self.cfg['mkvmerge'] = os.path.normpath(f)
            save_settings(self.cfg)
            return self.cfg['mkvmerge']
        return None

    def mkv_args(self, tracks, out, tag_files):
        args = ['--output', out]
        for t in tracks:
            if t['path'] in tag_files:
                args += ['--tags', f'0:{tag_files[t["path"]]}']
            args += ['--language', f'0:{t["lang"]}']
            if t['name']:
                args += ['--track-name', f'0:{t["name"]}']
            args += ['--default-track-flag', f'0:{"yes" if t["default"] else "no"}',
                     '--forced-display-flag', f'0:{"yes" if t["forced"] else "no"}',
                     '--hearing-impaired-flag', f'0:{"yes" if t["sdh"] else "no"}']
            if t['delay']:
                args += ['--sync', f'0:{t["delay"]}']
            if t['src'].lower().endswith('.srt'):
                args += ['--sub-charset', '0:UTF-8']
            args.append(t['src'])
        return args

    def export_mkv(self, tracks, out):
        fmt = self.out_fmt.get()
        if not out.lower().endswith(('.mks', '.mkv')):
            out += '.' + fmt
            self.out_path.set(out)
        missing = sorted({t['tags'] for t in tracks
                          if t['tags'].lower().endswith('.xml') and not os.path.isfile(t['tags'])})
        if missing:
            messagebox.showerror('SubPicker', 'Tags file not found:\n' + '\n'.join(missing))
            return
        exe = self.get_mkvmerge()
        if not exe:
            return
        if os.path.exists(out) and not messagebox.askyesno('SubPicker', f'{out}\nalready exists. Overwrite it?'):
            return
        os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
        tmpdir = tempfile.mkdtemp(prefix='subpicker_')
        try:
            tag_files, made = {}, {}
            for t in tracks:
                v = t['tags']
                if not v:
                    continue
                if is_xml_path(v):
                    tag_files[t['path']] = v
                else:                                   # raw text -> generated Source tags XML
                    if v not in made:
                        made[v] = os.path.join(tmpdir, f'source_{len(made) + 1}.xml')
                        with open(made[v], 'w', encoding='utf-8') as fh:
                            fh.write(source_xml(v))
                    tag_files[t['path']] = made[v]
            opt = os.path.join(tmpdir, 'options.json')
            with open(opt, 'w', encoding='utf-8') as fh:
                json.dump(self.mkv_args(tracks, out, tag_files), fh, ensure_ascii=False)
            self.root.config(cursor='watch')
            self.root.update()
            p = subprocess.run([exe, '--output-charset', 'UTF-8', '@' + opt], capture_output=True,
                               encoding='utf-8', errors='replace', creationflags=NO_WINDOW)
        except OSError as ex:
            messagebox.showerror('SubPicker', f'Could not run mkvmerge:\n{ex}')
            return
        finally:
            self.root.config(cursor='')
            shutil.rmtree(tmpdir, ignore_errors=True)
        lines = [ln for ln in p.stdout.splitlines() if ln.strip() and not ln.startswith('Progress')]
        if p.returncode in (0, 1):
            msg = f'Created {os.path.basename(out)}\n{len(tracks)} subtitle tracks'
            warns = [ln for ln in lines if 'arning' in ln]
            if warns:
                msg += '\n\nWarnings:\n' + '\n'.join(warns[:6])
            if messagebox.askyesno('SubPicker', msg + '\n\nOpen the folder?'):
                self.open_file(os.path.dirname(os.path.abspath(out)))
        else:
            messagebox.showerror('SubPicker', 'mkvmerge failed:\n\n' + '\n'.join(lines[-8:]))


def main():
    if sys.platform == 'win32':
        try:
            import ctypes
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except Exception:
            pass
    root = tk.Tk()
    if FROZEN:
        try:
            root.iconbitmap(default=sys.executable)
        except tk.TclError:
            pass
    folder = sys.argv[1] if len(sys.argv) > 1 else ''
    if not folder:
        cwd = os.getcwd()
        if any(f.lower().endswith(SUB_EXTS) for f in os.listdir(cwd)):
            folder = cwd
    App(root, folder)
    root.mainloop()


if __name__ == '__main__':
    main()
