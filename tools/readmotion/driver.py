"""Plays DeskPicker while readmotion.json records it (Windows only).

Puts the window at the spot the recording grabs, then glides the real mouse through the
app: the profiles, adding one, a profile's commit identity, and opening one. Points are
in physical pixels from the window's top-left, for the size set in PLACE.

    python driver.py          play it (readmotion starts this once recording begins)
    python driver.py --place  just move the window there, for measuring
"""
import ctypes
import ctypes.wintypes as wt
import sys
import time

u = ctypes.windll.user32
u.SetProcessDPIAware()

TITLE = "DeskPicker"
PLACE = (40, 40, 1540, 940)      # x, y, w, h of the window: readmotion.json's region matches
X0, Y0 = PLACE[0], PLACE[1]


def find(title, timeout=10.0):
    end = time.time() + timeout
    while time.time() < end:
        h = u.FindWindowW(None, title)
        if h:
            return h
        time.sleep(0.1)
    sys.exit(f"no window called {title}")


def place(h):
    u.ShowWindow(h, 9)                       # restore
    u.MoveWindow(h, *PLACE, True)
    u.SetForegroundWindow(h)


def pos():
    p = wt.POINT()
    u.GetCursorPos(ctypes.byref(p))
    return p.x, p.y


def glide(x, y, secs=0.6):
    """Eases the mouse to (x, y), relative to the window."""
    sx, sy = pos()
    tx, ty = X0 + x, Y0 + y
    n = max(1, int(secs * 60))
    for i in range(1, n + 1):
        t = i / n
        t = t * t * (3 - 2 * t)
        u.SetCursorPos(int(sx + (tx - sx) * t), int(sy + (ty - sy) * t))
        time.sleep(secs / n)


def click(x, y, secs=0.6, after=0.8):
    glide(x, y, secs)
    time.sleep(0.12)
    u.mouse_event(0x02, 0, 0, 0, 0)
    time.sleep(0.06)
    u.mouse_event(0x04, 0, 0, 0, 0)
    time.sleep(after)


def key(vk, after=0.5):
    u.keybd_event(vk, 0, 0, 0)
    time.sleep(0.03)
    u.keybd_event(vk, 0, 2, 0)
    time.sleep(after)


def type_text(text, per=0.09):
    for ch in text:
        r = u.VkKeyScanW(ord(ch))
        vk, shift = r & 0xFF, (r >> 8) & 1
        if shift:
            u.keybd_event(0x10, 0, 0, 0)
        key(vk, per)
        if shift:
            u.keybd_event(0x10, 0, 2, 0)


def wait(s):
    time.sleep(s)


def windows_called(title):
    found = []
    cb = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)

    def each(h, _):
        n = u.GetWindowTextLengthW(h)
        buf = ctypes.create_unicode_buffer(n + 1)
        u.GetWindowTextW(h, buf, n + 1)
        if buf.value == title and u.IsWindowVisible(h):
            found.append(h)
        return True
    u.EnumWindows(cb(each), 0)
    return found


BEAT = 5.0       # seconds per beat; readmotion.json cuts one clip per beat
LEAD = 1.0       # before the first beat, while the window settles


def play():
    h = find(TITLE)
    place(h)
    u.SetCursorPos(X0 + 1000, Y0 + 640)
    t0 = time.time()

    def at(beat, offset=0.0):
        wait(max(0.0, t0 + LEAD + beat * BEAT + offset - time.time()))

    # 0. the profiles: hover along the cards
    at(0, 0.3)
    glide(300, 380, 0.9); wait(0.4)
    glide(770, 380, 0.9); wait(0.4)
    glide(1230, 380, 0.9)
    # 1. a new profile gets its own folder, named as you type
    at(1)
    click(303, 700, after=0.9)
    type_text("Client")
    at(1, 4.6)
    key(0x1B, after=0.1)                     # Esc
    # 2. each profile commits as its own name and email
    at(2)
    click(780, 475, after=0.7)               # Work's "..." menu
    click(545, 549, after=0.2)               # Edit...
    at(2, 4.6)
    key(0x1B, after=0.1)
    # 3. open one: its own GitHub Desktop, running next to the others
    at(3)
    before = set(windows_called("GitHub Desktop"))
    click(194, 476, after=0.2)               # Personal's Open
    end = time.time() + 3.5
    while time.time() < end:
        new = [w for w in windows_called("GitHub Desktop") if w not in before]
        if new:
            wait(0.9)                        # seen arriving
            for w in new:
                u.ShowWindow(w, 6)           # minimize, so DeskPicker shows it running
            u.SetForegroundWindow(h)
            break
        wait(0.1)
    glide(330, 300, 0.8)
    at(3, 5.0)
    for w in windows_called("GitHub Desktop"):
        if w not in before:
            u.PostMessageW(w, 0x0010, 0, 0)  # WM_CLOSE: only the demo profile's copy


if __name__ == "__main__":
    if "--place" in sys.argv:
        place(find(TITLE))
    else:
        play()
