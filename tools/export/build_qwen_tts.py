"""Builds Qwen TTS's engine for a release: qwentts.cpp's tts-server, for the processor, at the commit the voices were
tried on, with the processor's features found when it runs (GGML_CPU_ALL_VARIANTS). Run it once before release.py:

    python tools/export/build_qwen_tts.py [--source <a checkout at that commit>] [--out <folder>] [--work <folder>]

It clones the source into builds/qwen-tts-src (with --source, it builds that checkout instead, after checking its
commit), builds it, and puts tts-server.exe, its libraries and their licences in builds/qwen-tts. It needs Git, Visual
Studio's C++ tools and CMake (on PATH, or named by the CMAKE environment variable). It downloads only that source.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REPO = 'https://github.com/ServeurpersoCom/qwentts.cpp'
COMMIT = 'de2ad833e9bd5c06082231d1da637789f7562816'
FLAGS = ['-DBUILD_SHARED_LIBS=ON', '-DGGML_BACKEND_DL=ON', '-DGGML_CPU_ALL_VARIANTS=ON', '-DGGML_NATIVE=OFF', '-DGGML_CCACHE=OFF']


def run(*args, cwd=None):
    print('>', ' '.join(str(a) for a in args), flush=True)
    done = subprocess.run([str(a) for a in args], cwd=cwd)
    if done.returncode != 0:
        sys.exit(f'failed ({done.returncode}): {args[0]}')


def commit_of(checkout):
    """The commit a checkout is at, read from its .git folder so it works without git."""
    git = checkout / '.git'
    head = (git / 'HEAD').read_text().strip()
    if not head.startswith('ref: '):
        return head
    ref = head[5:]
    if (git / ref).exists():
        return (git / ref).read_text().strip()
    return next(line.split()[0] for line in (git / 'packed-refs').read_text().splitlines() if line.endswith(' ' + ref))


def source(args):
    if args.source:
        checkout = Path(args.source).resolve()
        if commit_of(checkout) != COMMIT:
            sys.exit(f'{checkout} is at {commit_of(checkout)}, not {COMMIT}')
        return checkout
    checkout = ROOT / 'builds' / 'qwen-tts-src'
    if not checkout.exists():
        run('git', 'clone', REPO, checkout)
    run('git', 'checkout', '--detach', COMMIT, cwd=checkout)
    run('git', 'submodule', 'update', '--init', '--recursive', cwd=checkout)
    return checkout


def yyjson_licence(checkout):
    """yyjson keeps its MIT notice at the top of its header rather than in a file of its own."""
    header = (checkout / 'vendor' / 'yyjson' / 'yyjson.h').read_text(encoding='utf-8')
    comment = re.match(r'/\*=+\n(.*?)\n \*=+\*/', header, re.S)
    return re.sub(r'(?m)^ ', '', comment.group(1)).strip() + '\n'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source')
    parser.add_argument('--out', default=str(ROOT / 'builds' / 'qwen-tts'))
    parser.add_argument('--work', default=str(ROOT / 'builds' / 'qwen-tts-build'))
    args = parser.parse_args()

    cmake = os.environ.get('CMAKE') or shutil.which('cmake')
    if not cmake:
        sys.exit('CMake not found: put it on PATH, or name it in the CMAKE environment variable')
    checkout = source(args)
    work, out = Path(args.work).resolve(), Path(args.out).resolve()

    run(cmake, '-S', checkout, '-B', work, '-A', 'x64', *FLAGS)
    run(cmake, '--build', work, '--config', 'Release', '--parallel')

    built = work / 'Release'
    libraries = sorted(built.glob('*.dll'))
    assert (built / 'tts-server.exe').exists(), 'the build made no tts-server.exe'
    assert any(re.fullmatch(r'ggml-cpu-.+\.dll', f.name) for f in libraries), 'no CPU variants: GGML_CPU_ALL_VARIANTS did not take'
    if out.exists():
        shutil.rmtree(out)
    (out / 'licences').mkdir(parents=True)
    for f in [built / 'tts-server.exe', *libraries]:
        shutil.copy(f, out / f.name)
    shutil.copy(checkout / 'LICENSE', out / 'licences' / 'qwentts.cpp.txt')
    shutil.copy(checkout / 'ggml' / 'LICENSE', out / 'licences' / 'ggml.txt')
    shutil.copy(checkout / 'vendor' / 'cpp-httplib' / 'LICENSE', out / 'licences' / 'cpp-httplib.txt')
    (out / 'licences' / 'yyjson.txt').write_text(yyjson_licence(checkout), encoding='utf-8')
    size = sum(f.stat().st_size for f in out.iterdir() if f.is_file())
    print(f'{out}: tts-server.exe and {len(libraries)} libraries, {size / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
