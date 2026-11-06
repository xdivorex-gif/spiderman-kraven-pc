"""Read files from an Xbox disc image (XDVDFS), without extracting it all.

    py -3 tools/xiso.py <image.iso> [path ...]     list, or extract paths here

Works on a full ("redump") image, where the game partition starts at one of
OFFSETS, and on a trimmed xiso (partition at 0).

XDVDFS: 2048-byte sectors. Sector 32 of the partition holds the volume
descriptor: "MICROSOFT*XBOX*MEDIA", u32 root directory sector, u32 root
directory size. A directory is a binary tree of entries, each 4-aligned:
u16 left / u16 right (child offsets in 4-byte units, 0 = none), u32 start
sector, u32 size, u8 attributes (0x10 = directory), u8 name length, name.
"""
import os
import struct
import sys

MAGIC = b'MICROSOFT*XBOX*MEDIA'
SECTOR = 2048
OFFSETS = (0, 0x18300000, 0xFD90000, 0x2080000)


class Image:
    def __init__(self, path):
        self.f = open(path, 'rb')
        for off in OFFSETS:
            self.f.seek(off + 32 * SECTOR)
            head = self.f.read(28)
            if head[:20] == MAGIC:
                self.base = off
                self.root = struct.unpack_from('<2I', head, 20)
                break
        else:
            raise ValueError('%s: not an Xbox disc image (no XDVDFS volume found)' % path)
        self.index = {}
        self._walk(self.root[0], self.root[1], '')

    def _read(self, sector, size):
        self.f.seek(self.base + sector * SECTOR)
        return self.f.read(size)

    def _walk(self, sector, size, prefix):
        if size == 0:
            return
        d = self._read(sector, size)
        todo = [0]
        seen = set()
        while todo:
            o = todo.pop()
            if o in seen or o + 14 > len(d):
                continue
            seen.add(o)
            left, right, start, length, attr, n = struct.unpack_from('<HHIIBB', d, o)
            if left == 0xFFFF:          # padding
                continue
            name = d[o + 14:o + 14 + n].decode('latin1')
            path = prefix + name
            if attr & 0x10:
                self._walk(start, length, path + '/')
            else:
                self.index[path.lower()] = (path, start, length)
            if left:
                todo.append(left * 4)
            if right:
                todo.append(right * 4)

    def files(self):
        return sorted(p for p, _, _ in self.index.values())

    def read(self, path):
        hit = self.index.get(path.replace('\\', '/').lower())
        if hit is None:
            raise KeyError('%s not on the disc' % path)
        _, start, length = hit
        return self._read(start, length)

    def extract(self, path, dst):
        os.makedirs(os.path.dirname(dst) or '.', exist_ok=True)
        with open(dst, 'wb') as f:
            f.write(self.read(path))


if __name__ == '__main__':
    img = Image(sys.argv[1])
    if len(sys.argv) == 2:
        for p in img.files():
            print(p)
    for p in sys.argv[2:]:
        img.extract(p, os.path.basename(p))
