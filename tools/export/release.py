"""Builds a release of Beside the Cabinet to share: the Godot export with no GPL code in it, every notice it owes, and a
zip. Run from anywhere, with the version:

    python tools/export/release.py 0.1.0-preview.1

It exports into builds/release/<version>/BesideTheCabinet/ (git-ignored) with the Godot that GODOT, or private\\local.cmd,
names (export templates for the same version needed), then swaps in sherpa-onnx's own build without text-to-speech: the
NuGet one links espeak-ng (GPL-3.0) for speech the app never asks it for; only its listening is used. It writes
THIRD-PARTY-NOTICES.txt from what actually shipped, and stops on any library it can't account for or any trace of
espeak-ng; adds LICENSE.md, NOTICE.md and a README.txt; and zips it all as BesideTheCabinet-<version>-windows-x64.zip.
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GAME = ROOT / 'src' / 'InsertCoin.Game'
HERE = Path(__file__).resolve().parent
NUGET = Path.home() / '.nuget' / 'packages'
REPO = 'https://github.com/BobZheng00/Beside-The-Cabinet'

# sherpa-onnx's build without text-to-speech, from its own release, for the version the game's NuGet package has
SHERPA_VERSION = '1.13.8'
SHERPA_URL = ('https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/'
              'sherpa-onnx-v1.13.8-win-x64-shared-MT-Release-no-tts-lib.tar.bz2')
SHERPA_SHA256 = 'a1253e665c4f236119c443c8932a8acfca32a546c78c05962d483c9a0eae21b7'
SHERPA_DLL = 'sherpa-onnx-v1.13.8-win-x64-shared-MT-Release-no-tts-lib/lib/sherpa-onnx-c-api.dll'
ESPEAK = [b'phontab', b'phondata', b'phonindex']          # espeak-ng's data files: where its code is, they are named

BUILD_ONLY = {'GodotSharpEditor', 'Godot.SourceGenerators'}


def run(*args, **kw):
    done = subprocess.run([str(a) for a in args], capture_output=True, text=True, encoding='utf-8', errors='replace', **kw)
    if done.returncode != 0:
        sys.exit(f'failed ({done.returncode}): {" ".join(map(str, args))}\n{done.stdout[-2000:]}\n{done.stderr[-2000:]}')
    return done.stdout


def godot_console():
    local = ROOT / 'private' / 'local.cmd'
    godot = None
    if local.exists():
        found = re.search(r'set "GODOT=([^"]+)"', local.read_text(encoding='utf-8', errors='replace'))
        godot = found and found.group(1)
    godot = os.environ.get('GODOT') or godot
    if not godot:
        sys.exit('Set GODOT to your Godot 4.7.2 .NET editor (its .exe), or put set "GODOT=..." in private\\local.cmd.')
    console = Path(re.sub(r'\.exe$', '_console.exe', godot))
    return console if console.exists() else Path(godot)


def packages():
    """The NuGet packages the game ships with (id, version): its own and theirs, for Windows x64 only."""
    listing = json.loads(run('dotnet', 'list', GAME / 'InsertCoin.Game.csproj', 'package', '--include-transitive',
                             '--format', 'json'))
    found = {}
    for framework in listing['projects'][0]['frameworks']:
        for kind in ('topLevelPackages', 'transitivePackages'):
            for p in framework.get(kind, []):
                found[p['id']] = p['resolvedVersion']
    return {i: v for i, v in found.items() if i not in BUILD_ONLY
            and not (i.startswith('org.k2fsa.sherpa.onnx.runtime.') and not i.endswith('win-x64'))}


def nuspec(folder):
    text = next(folder.glob('*.nuspec')).read_text(encoding='utf-8', errors='replace')
    tag = lambda t: (re.search(rf'<{t}[^>]*>([^<]+)</{t}>', text) or [None, None])[1]
    kind = (re.search(r'<license type="(\w+)">', text) or [None, None])[1]
    return tag('license'), kind, tag('copyright'), tag('licenseUrl')


def licence_text(spdx, copyright=None):
    text = (HERE / 'licences' / f'{spdx}.txt').read_text(encoding='utf-8').strip()
    if copyright:                                        # the template's own placeholder line, made the project's
        text = re.sub(r'(?m)^Copyright \(c\) <year> <[^>]+>\.?[ \t]*$', copyright, text, count=1)
        assert '<year>' not in text, 'the licence template kept its placeholder'
    return text


def dlls_of(folder):
    """File names a package or runtime pack can put beside the app on Windows x64."""
    names = set()
    for sub in ['lib', 'runtimes/win-x64', 'runtimes/win']:
        if (folder / sub).exists():
            names |= {f.name.lower() for f in (folder / sub).rglob('*') if f.suffix.lower() in ('.dll', '.exe')}
    return names


def export(app):
    if app.exists():
        shutil.rmtree(app)
    app.mkdir(parents=True)
    run(godot_console(), '--headless', '--path', GAME, '--export-release', 'Windows Desktop', app / 'BesideTheCabinet.exe')
    if not (app / 'BesideTheCabinet.exe').exists():
        sys.exit('the export made no BesideTheCabinet.exe')
    return next(app.glob('data_*'))


def without_tts(data, downloads):
    archive = downloads / SHERPA_URL.rsplit('/', 1)[1]
    if not archive.exists():
        downloads.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(SHERPA_URL, archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != SHERPA_SHA256:
        sys.exit(f'{archive.name} is not the file that was checked: delete it and run again')
    dll = tarfile.open(archive).extractfile(SHERPA_DLL).read()
    assert b'SherpaOnnxCreateOfflineRecognizer' in dll and not any(w in dll for w in ESPEAK)
    (data / 'sherpa-onnx-c-api.dll').write_bytes(dll)
    for f in data.glob('*.lib'):                         # import libraries: only for building against them
        f.unlink()


def notices(app, data, version, shipped):
    out = [f'Beside the Cabinet {version}: third-party notices', '',
           'Beside the Cabinet is Copyright 2026 Shenyan Zheng, under the PolyForm Noncommercial License 1.0.0 (LICENSE.md),',
           'with the exceptions NOTICE.md lists. It includes the work of others, with their notices and licences below.', '']
    section = lambda title: out.extend(['', '=' * 100, title, '=' * 100, ''])
    known = {'insertcoin.core.dll', 'insertcoin.adapters.dll', 'insertcoin.mind.dll', 'insertcoin.game.dll'}

    section('Godot Engine (the app runs on it): its licence and the third-party code in it, as the engine reports them')
    report = app.parent / 'godot-licences.txt'
    run(godot_console(), '--headless', '--script', HERE / 'licences.gd', '--', report)
    out.append(report.read_text(encoding='utf-8').strip())
    report.unlink()

    runtime = json.loads(next(data.glob('*.runtimeconfig.json')).read_text())['runtimeOptions']['includedFrameworks'][0]
    pack = NUGET / 'microsoft.netcore.app.runtime.win-x64' / runtime['version']
    known |= dlls_of(pack)
    section(f'.NET runtime {runtime["version"]} (Microsoft.NETCore.App)')
    out += [(pack / 'LICENSE.TXT').read_text(encoding='utf-8').strip(), '',
            (pack / 'THIRD-PARTY-NOTICES.TXT').read_text(encoding='utf-8').strip()]

    by_licence, seen = {}, {}
    section('Libraries (NuGet packages)')
    for pid, ver in sorted(shipped.items(), key=lambda p: p[0].lower()):
        folder = NUGET / pid.lower() / ver
        known |= dlls_of(folder)
        if pid == 'GodotSharp':
            continue                                     # Godot's own: above
        lic, kind, copyright, url = nuspec(folder)
        out += [f'--- {pid} {ver}', copyright or '']
        if pid == 'org.k2fsa.sherpa.onnx.runtime.win-x64':
            out.append(f'Its sherpa-onnx-c-api.dll here is sherpa-onnx\'s own build without text-to-speech: {SHERPA_URL}')
        if kind == 'file':
            out.append((folder / lic).read_text(encoding='utf-8', errors='replace').strip())
        elif lic in ('MIT', 'BSD-2-Clause', 'BSD-3-Clause'):
            out.append(licence_text(lic, copyright and re.sub(r'^(?:Copyright\s*)?(?:\(c\)\s*|©\s*)?', 'Copyright (c) ', copyright.strip())))
        elif lic:
            out.append(f'Licence: {lic} (its text below)')
            by_licence.setdefault(lic, []).append(pid)
        else:
            sys.exit(f'{pid} {ver}: no licence found in its package')
        for extra in sorted(folder.iterdir()):
            if re.match(r'(?i)(third.?party.?notices|notice)', extra.name) and extra.is_file():
                text = extra.read_text(encoding='utf-8', errors='replace').strip()
                first = seen.setdefault(text, pid)          # many packages carry the same notices: each is given once
                out += ['', f'{extra.name}:', text if first == pid else f'(the same as {first}\'s, above)']
        out.append('')
    windows_sdk = NUGET / 'microsoft.windows.sdk.net.ref'
    for folder in windows_sdk.glob('*'):
        known |= dlls_of(folder)
    lic, kind, copyright, url = nuspec(max(windows_sdk.glob('*'), key=lambda f: f.name))
    out += ['--- Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll (the Windows SDK\'s projection for .NET)', copyright,
            f'Licence: the Windows SDK\'s terms, {url}', '',
            '--- SQLite (in e_sqlite3.dll, through SQLitePCLRaw)', 'SQLite is in the public domain: https://sqlite.org/copyright.html', '']
    for lic, ids in by_licence.items():
        section(f'{lic}, for {", ".join(ids)}')
        out.append(licence_text(lic))

    section('Models (in the app, under models/)')
    for f in sorted((GAME / 'models').glob('*/LICENSE.txt')):
        out += [f'--- {f.parent.name}', f.read_text(encoding='utf-8', errors='replace').strip(), '']
    section('Fonts (in the app, under fonts/)')
    for f in sorted((GAME / 'fonts').glob('*.txt')):
        out += [f'--- {f.name}', f.read_text(encoding='utf-8', errors='replace').strip(), '']
    section('Godot add-ons (in the app, under addons/)')
    for f in sorted((GAME / 'addons').glob('*/LICENSE*')):
        out += [f'--- {f.parent.name}', f.read_text(encoding='utf-8', errors='replace').strip(), '']
    section('Characters and their voices')
    for f in sorted((GAME / 'characters').glob('*/SOURCE.txt')):
        out += [f'--- {f.parent.name}', f.read_text(encoding='utf-8', errors='replace').strip(), '']
    out += ['Shino\'s and Fumiriya\'s voices were made with Kokoro-82M (hexgrad, Apache-2.0): https://huggingface.co/hexgrad/Kokoro-82M']

    unknown = sorted(f.name for f in data.iterdir() if f.suffix.lower() in ('.dll', '.exe') and f.name.lower() not in known)
    if unknown:
        sys.exit(f'not accounted for in the notices: {", ".join(unknown)}')
    for f in [app / 'BesideTheCabinet.exe', *data.glob('*.dll')]:
        if any(w in f.read_bytes() for w in ESPEAK):
            sys.exit(f'{f.name} still carries espeak-ng (GPL-3.0)')
    (app / 'THIRD-PARTY-NOTICES.txt').write_text('\n'.join(out).rstrip() + '\n', encoding='utf-8')


README = """Beside the Cabinet {version}

A companion who keeps you company beside a little arcade cabinet on your desktop.
This is a preview: an early build to try, with rough edges.

Running it
- Keep this folder together (BesideTheCabinet.exe and the data_ folder beside it) and run BesideTheCabinet.exe.
- The app isn't signed yet, so Windows may say it protected your PC: More info, then Run anyway.
- The first run asks your name, who keeps you company, and which AI to use: your own key for OpenAI, Anthropic,
  OpenRouter, DeepSeek or Qwen, or a model running on your own PC (Ollama, LM Studio).
- Talking aloud and memory search need Microsoft's Visual C++ runtime, which most PCs already have. If either fails,
  install it from Microsoft: https://aka.ms/vs/17/release/vc_redist.x64.exe
- A voice needs an NVIDIA graphics card: Settings, then Voice, can install GPT-SoVITS for you (8 GB).

Where things are kept
- Your profile: %APPDATA%\\InsertCoin (settings, memory; your keys encrypted for your Windows account).
- The log, for a bug report: %APPDATA%\\Godot\\app_userdata\\Beside the Cabinet\\logs\\godot.log
- To remove it all: untick "Start with Windows" if you ticked it, then delete this folder and %APPDATA%\\InsertCoin.

Problems and ideas: {repo}/issues
Licence: LICENSE.md (PolyForm Noncommercial 1.0.0) and NOTICE.md; others' work: THIRD-PARTY-NOTICES.txt.
"""


def main():
    found = len(sys.argv) == 2 and re.fullmatch(r'(\d+\.\d+\.\d+)(?:-[a-z]+\.(\d+))?', sys.argv[1])
    if not found:
        sys.exit('usage: python tools/export/release.py <version>, such as 0.1.0-preview.1 or 0.1.0')
    version = sys.argv[1]
    exe_version = f'{found[1]}.{found[2] or 0}'          # Windows wants four numbers: 0.1.0-preview.1 is 0.1.0.1
    preset = (GAME / 'export_presets.cfg').read_text(encoding='utf-8')
    if f'application/file_version="{exe_version}"' not in preset or f'application/product_version="{exe_version}"' not in preset:
        sys.exit(f'set application/file_version and application/product_version to "{exe_version}" in export_presets.cfg')
    shipped = packages()
    if shipped.get('org.k2fsa.sherpa.onnx') != SHERPA_VERSION:
        sys.exit(f'the game has sherpa-onnx {shipped.get("org.k2fsa.sherpa.onnx")}: pin its build without text-to-speech')
    out = ROOT / 'builds' / 'release' / version
    app = out / 'BesideTheCabinet'
    print('exporting…')
    data = export(app)
    without_tts(data, ROOT / 'builds' / 'release' / 'downloads')
    print('notices…')
    notices(app, data, version, shipped)
    for name in ('LICENSE.md', 'NOTICE.md'):
        shutil.copy(ROOT / name, app / name)
    (app / 'README.txt').write_text(README.format(version=version, repo=REPO), encoding='utf-8')
    archive = out / f'BesideTheCabinet-{version}-windows-x64.zip'
    print('zipping…')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for f in sorted(app.rglob('*')):
            if f.is_file():
                z.write(f, Path('BesideTheCabinet') / f.relative_to(app))
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    print(f'{archive}\n{archive.stat().st_size / 1e6:.0f} MB, SHA-256 {digest}')


if __name__ == '__main__':
    main()
