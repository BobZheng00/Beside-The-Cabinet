"""Fetches only the Windows export templates out of Godot's (1.2 GB, all-platform) templates archive.

The archive is a zip on GitHub, which supports HTTP range requests, so the index can be read and single members
downloaded without fetching the rest.

    python tools/export/fetch_template.py --list            # show members and sizes (reads the index only)
    python tools/export/fetch_template.py --get DEST_DIR    # download the Windows x86_64 release template(s)
"""
import io
import sys
import urllib.request
import zipfile
from pathlib import Path

VERSION = "4.7.2-stable"
URL = f"https://github.com/godotengine/godot-builds/releases/download/{VERSION}/Godot_v{VERSION}_mono_export_templates.tpz"
WANTED = ("templates/windows_release_x86_64.exe", "templates/windows_release_x86_64_console.exe", "templates/version.txt")


class HttpRangeFile(io.RawIOBase):
    """A seekable read-only file over HTTP range requests."""

    def __init__(self, url):
        request = urllib.request.Request(url, method="HEAD")
        with urllib.request.urlopen(request) as response:
            self.url = response.geturl()                      # follow the redirect once
            self.size = int(response.headers["Content-Length"])
        self.pos = 0
        self.fetched = 0

    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos

    def seek(self, offset, whence=io.SEEK_SET):
        self.pos = {io.SEEK_SET: offset, io.SEEK_CUR: self.pos + offset, io.SEEK_END: self.size + offset}[whence]
        return self.pos

    def read(self, n=-1):
        if n < 0:
            n = self.size - self.pos
        if n == 0 or self.pos >= self.size:
            return b""
        end = min(self.pos + n, self.size) - 1
        request = urllib.request.Request(self.url, headers={"Range": f"bytes={self.pos}-{end}"})
        with urllib.request.urlopen(request) as response:
            data = response.read()
        self.pos += len(data)
        self.fetched += len(data)
        return data

    def readinto(self, buffer):
        data = self.read(len(buffer))
        buffer[:len(data)] = data
        return len(data)


def main():
    remote = HttpRangeFile(URL)
    archive = zipfile.ZipFile(io.BufferedReader(remote, buffer_size=1 << 20))
    if "--list" in sys.argv:
        print(f"archive: {remote.size / 1e6:.0f} MB")
        for info in archive.infolist():
            mark = "*" if info.filename in WANTED else " "
            print(f"{mark} {info.filename:55} {info.compress_size / 1e6:8.1f} MB compressed  {info.file_size / 1e6:8.1f} MB")
        print(f"index read: {remote.fetched / 1e3:.0f} KB")
        return
    dest = Path(sys.argv[sys.argv.index("--get") + 1])
    dest.mkdir(parents=True, exist_ok=True)
    for name in WANTED:
        target = dest / Path(name).name
        with archive.open(name) as source, open(target, "wb") as out:
            while chunk := source.read(1 << 22):
                out.write(chunk)
        print(f"wrote {target} ({target.stat().st_size / 1e6:.1f} MB)")
    print(f"downloaded {remote.fetched / 1e6:.1f} MB of {remote.size / 1e6:.0f} MB")


if __name__ == "__main__":
    main()
