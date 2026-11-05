"""Exercise Scrooge's tray callback against an already running, populated Windows dashboard."""
import ctypes
from ctypes import wintypes
import time
from PIL import ImageGrab, ImageChops, ImageStat

win = ctypes.windll.user32
win.SetProcessDPIAware()
window = win.FindWindowW(None, "AI Token Scrooge")
assert window, "Open the Scrooge dashboard before running this check."
def wait(check, seconds):
    end = time.perf_counter() + seconds
    while not check():
        if time.perf_counter() > end:
            raise RuntimeError("Window did not reach expected state")
        time.sleep(0.01)
def sample():
    rect = wintypes.RECT()
    win.GetWindowRect(window, ctypes.byref(rect))
    return ImageGrab.grab((rect.left + 65, rect.top + 70, rect.left + 660, rect.top + 490), all_screens=True).convert("RGB")
# Verify the actual tray-message handler rather than restoring via ShowWindow.
win.PostMessageW(window, 0x8001, 1, 0x0202)
wait(lambda: win.GetForegroundWindow() == window and not win.IsIconic(window), 10)
time.sleep(1)
baseline = sample()
assert max(ImageStat.Stat(baseline).stddev) > 20, "Reference dashboard appears blank"
win.PostMessageW(window, 0x0112, 0xF020, 0)  # WM_SYSCOMMAND, SC_MINIMIZE
wait(lambda: win.IsIconic(window), 4)
time.sleep(0.5)
start = time.perf_counter()
win.PostMessageW(window, 0x8001, 1, 0x0202)
wait(lambda: not win.IsIconic(window) and win.GetForegroundWindow() == window, 5)
for _ in range(30):
    delta = ImageStat.Stat(ImageChops.difference(sample(), baseline)).mean
    if max(delta) < 4:
        break
else:
    raise AssertionError("Restored dashboard did not match its populated pre-minimize view")
elapsed = time.perf_counter() - start
print(f"Populated header and dashboard matched after {elapsed:.3f}s")
assert elapsed < 2, "Dashboard took at least two seconds to reappear"
